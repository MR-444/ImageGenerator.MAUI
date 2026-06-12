using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Civitai;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services.Civitai;

public sealed class CivitaiPostingServiceTests : IDisposable
{
    private const string UploadResponse =
        """{"result":{"content":[{"type":"text","text":"u-123"}],"structuredContent":{"uuid":"u-123","width":1536,"height":1024,"contentType":"image/png"}},"jsonrpc":"2.0","id":1}""";

    private const string CreatePostResponse =
        """{"result":{"data":{"json":{"id":29167625,"title":"t","publishedAt":null,"imageIds":[1]}}}}""";

    private const string WhoamiResponse =
        """{"result":{"content":[{"type":"text","text":"You are Silmas (id 1)."}],"structuredContent":{"id":1,"username":"Silmas","isOnboarded":true,"muted":false,"tier":null}},"jsonrpc":"2.0","id":1}""";

    // Minimal real PNG signature so DetectImageMimeType sniffs image/png.
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private readonly string _tempDir;
    private readonly string _imagePath;
    private readonly Mock<ICivitaiTokenStore> _tokenStore = new();
    private readonly RecordingHandler _handler = new();
    private readonly CivitaiPostingService _sut;

    public CivitaiPostingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "civitai-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _imagePath = Path.Combine(_tempDir, "image.png");
        File.WriteAllBytes(_imagePath, PngBytes);

        _tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync("test-key");
        // Fresh client per CreateClient (sharing the handler): the service disposes each
        // client after use, exactly like the real IHttpClientFactory contract allows.
        _sut = new CivitaiPostingService(
            new StubHttpClientFactory(() => new HttpClient(_handler, disposeHandler: false)),
            _tokenStore.Object,
            NullLogger<CivitaiPostingService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ---- PostImageAsync ----

    [Fact]
    public async Task PostImage_HappyPath_UploadsThenCreatesDraft()
    {
        _handler.Enqueue(HttpStatusCode.OK, UploadResponse);
        _handler.Enqueue(HttpStatusCode.OK, CreatePostResponse);

        var meta = new Dictionary<string, object> { ["prompt"] = "a fox", ["seed"] = 42L };
        var result = await _sut.PostImageAsync(_imagePath, "My title", meta, modelVersionId: null);

        result.Success.Should().BeTrue();
        result.PostId.Should().Be(29167625);
        result.PostUrl.Should().Be("https://civitai.com/posts/29167625");

        _handler.Requests.Should().HaveCount(2);

        // Request 1: MCP upload_image with the file as base64 + Bearer auth.
        var (uploadUri, uploadAuth, uploadBody) = _handler.Requests[0];
        uploadUri.Should().Be("https://mcp.civitai.com/mcp");
        uploadAuth.Should().Be("Bearer test-key");
        using (var doc = JsonDocument.Parse(uploadBody))
        {
            var root = doc.RootElement;
            root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
            root.GetProperty("method").GetString().Should().Be("tools/call");
            var p = root.GetProperty("params");
            p.GetProperty("name").GetString().Should().Be("upload_image");
            p.GetProperty("arguments").GetProperty("data").GetString()
                .Should().Be(Convert.ToBase64String(PngBytes));
            p.GetProperty("arguments").GetProperty("contentType").GetString().Should().Be("image/png");
        }

        // Request 2: direct tRPC createWithImages with superjson envelope, draft + meta.
        var (createUri, createAuth, createBody) = _handler.Requests[1];
        createUri.Should().Be("https://civitai.com/api/trpc/post.createWithImages");
        createAuth.Should().Be("Bearer test-key");
        using (var doc = JsonDocument.Parse(createBody))
        {
            var input = doc.RootElement.GetProperty("json");
            input.GetProperty("title").GetString().Should().Be("My title");
            input.GetProperty("publish").GetBoolean().Should().BeTrue("one-step publish — user decision 2026-06-13");
            var image = input.GetProperty("images")[0];
            image.GetProperty("url").GetString().Should().Be("u-123", "the upload UUID rides in `url`");
            image.GetProperty("index").GetInt32().Should().Be(0);
            image.GetProperty("width").GetInt32().Should().Be(1536);
            image.GetProperty("height").GetInt32().Should().Be(1024);
            image.GetProperty("meta").GetProperty("prompt").GetString().Should().Be("a fox");
            image.GetProperty("meta").GetProperty("seed").GetInt64().Should().Be(42);
        }
    }

    [Fact]
    public async Task PostImage_NullMeta_OmitsMetaField()
    {
        _handler.Enqueue(HttpStatusCode.OK, UploadResponse);
        _handler.Enqueue(HttpStatusCode.OK, CreatePostResponse);

        var result = await _sut.PostImageAsync(_imagePath, "t", meta: null, modelVersionId: null);

        result.Success.Should().BeTrue();
        using var doc = JsonDocument.Parse(_handler.Requests[1].Body);
        var input = doc.RootElement.GetProperty("json");
        input.GetProperty("images")[0].TryGetProperty("meta", out _).Should().BeFalse();
        input.TryGetProperty("modelVersionId", out _).Should().BeFalse();
        input.GetProperty("images")[0].TryGetProperty("modelVersionId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PostImage_EmptyTitle_OmitsTitleField()
    {
        _handler.Enqueue(HttpStatusCode.OK, UploadResponse);
        _handler.Enqueue(HttpStatusCode.OK, CreatePostResponse);

        var result = await _sut.PostImageAsync(_imagePath, "", null, null);

        result.Success.Should().BeTrue();
        using var doc = JsonDocument.Parse(_handler.Requests[1].Body);
        doc.RootElement.GetProperty("json").TryGetProperty("title", out _).Should().BeFalse(
            "an empty title (e.g. JSON prompt with no usable description) is omitted, not sent as \"\"");
    }

    [Fact]
    public async Task PostImage_WithModelVersionId_SetsItOnPostAndImage()
    {
        _handler.Enqueue(HttpStatusCode.OK, UploadResponse);
        _handler.Enqueue(HttpStatusCode.OK, CreatePostResponse);

        var result = await _sut.PostImageAsync(_imagePath, "t", null, modelVersionId: 3005491);

        result.Success.Should().BeTrue();
        using var doc = JsonDocument.Parse(_handler.Requests[1].Body);
        var input = doc.RootElement.GetProperty("json");
        // Both placements mirror the MCP server's own wrapper — the post-level association is
        // what lands the published post in the model's gallery.
        input.GetProperty("modelVersionId").GetInt32().Should().Be(3005491);
        input.GetProperty("images")[0].GetProperty("modelVersionId").GetInt32().Should().Be(3005491);
    }

    [Fact]
    public async Task PostImage_DoesNotModifyTheLocalFile()
    {
        _handler.Enqueue(HttpStatusCode.OK, UploadResponse);
        _handler.Enqueue(HttpStatusCode.OK, CreatePostResponse);

        await _sut.PostImageAsync(_imagePath, "t", null, null);

        File.ReadAllBytes(_imagePath).Should().Equal(PngBytes,
            "the upload must be byte-identical and never touch the saved file");
    }

    [Fact]
    public async Task PostImage_UploadToolError_FailsWithoutCreatingPost()
    {
        _handler.Enqueue(HttpStatusCode.OK,
            """{"result":{"content":[{"type":"text","text":"Error: upload rejected"}],"structuredContent":{"ok":false,"error":"upload rejected"},"isError":true},"jsonrpc":"2.0","id":1}""");

        var result = await _sut.PostImageAsync(_imagePath, "t", null, null);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("upload rejected");
        _handler.Requests.Should().HaveCount(1, "create_post must not run after a failed upload");
    }

    [Fact]
    public async Task PostImage_JsonRpcError_FailsCleanly()
    {
        _handler.Enqueue(HttpStatusCode.OK,
            """{"jsonrpc":"2.0","id":1,"error":{"code":-32602,"message":"Input validation error"}}""");

        var result = await _sut.PostImageAsync(_imagePath, "t", null, null);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Input validation error");
    }

    [Fact]
    public async Task PostImage_TrpcErrorBody_SurfacesServerMessage()
    {
        _handler.Enqueue(HttpStatusCode.OK, UploadResponse);
        _handler.Enqueue(HttpStatusCode.BadRequest,
            """{"error":{"json":{"message":"You must be onboarded","code":-32600}}}""");

        var result = await _sut.PostImageAsync(_imagePath, "t", null, null);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("You must be onboarded");
    }

    [Fact]
    public async Task PostImage_Http500WithGarbageBody_FailsWithoutThrowing()
    {
        _handler.Enqueue(HttpStatusCode.InternalServerError, "<html>cloudflare sad</html>");

        var result = await _sut.PostImageAsync(_imagePath, "t", null, null);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("500");
    }

    [Fact]
    public async Task PostImage_UnparsableSuccessBody_FailsWithoutThrowing()
    {
        _handler.Enqueue(HttpStatusCode.OK, "not json at all");

        var result = await _sut.PostImageAsync(_imagePath, "t", null, null);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task PostImage_NoToken_FailsFastWithZeroRequests()
    {
        _tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync((string?)null);

        var result = await _sut.PostImageAsync(_imagePath, "t", null, null);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Settings");
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PostImage_MissingFile_FailsWithoutThrowing()
    {
        var result = await _sut.PostImageAsync(Path.Combine(_tempDir, "gone.png"), "t", null, null);

        result.Success.Should().BeFalse();
        _handler.Requests.Should().BeEmpty();
    }

    // ---- TestConnectionAsync ----

    [Fact]
    public async Task TestConnection_Success_ReportsUsername_TierNullHandled()
    {
        _handler.Enqueue(HttpStatusCode.OK, WhoamiResponse);

        var result = await _sut.TestConnectionAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Connected as Silmas.");
    }

    [Fact]
    public async Task TestConnection_BadKey_ReportsServerError()
    {
        _handler.Enqueue(HttpStatusCode.OK,
            """{"result":{"content":[{"type":"text","text":"Error: No API key available."}],"structuredContent":{"ok":false,"error":"No API key available."},"isError":true},"jsonrpc":"2.0","id":1}""");

        var result = await _sut.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No API key available");
    }

    [Fact]
    public async Task TestConnection_NoStoredKey_FailsFast()
    {
        _tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync(string.Empty);

        var result = await _sut.TestConnectionAsync();

        result.Success.Should().BeFalse();
        _handler.Requests.Should().BeEmpty();
    }

    // ---- SSE unwrap (defensive path) ----

    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("event: message\ndata: {\"a\":1}\n\n", "{\"a\":1}")]
    [InlineData("data: {\"a\":1}", "{\"a\":1}")]
    public void UnwrapSse_HandlesPlainAndStreamBodies(string body, string expected)
    {
        CivitaiPostingService.UnwrapSse(body).Should().Be(expected);
    }

    // ---- test handler ----

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<(string Uri, string? Authorization, string Body)> Requests { get; } = [];

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("Authorization", out var auth) ? auth.First() : null,
                body));

            var (status, responseBody) = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.InternalServerError, "no scripted response");
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
