using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ImageGenerator.MAUI.Tests.Services.ComfyUi;

public sealed class ComfyUiCheckpointServiceTests : IDisposable
{
    // Realistic /object_info/CheckpointLoaderSimple shape: ckpt_name is [names, options-dict].
    private const string ObjectInfoBody =
        """
        {
          "CheckpointLoaderSimple": {
            "input": {
              "required": {
                "ckpt_name": [
                  ["a.safetensors", "b.safetensors"],
                  { "tooltip": "The name of the checkpoint (model) to load." }
                ]
              }
            },
            "output": ["MODEL", "CLIP", "VAE"]
          }
        }
        """;

    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Loose);
    private readonly Mock<IUiStateStore> _uiState = new();
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly string _cacheDir;
    private readonly string _workflowDir;
    private readonly ComfyUiCheckpointService _service;

    private Func<HttpResponseMessage> _response = () => Json(ObjectInfoBody);

    public ComfyUiCheckpointServiceTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "imggen-comfy-ckpt-tests-" + Guid.NewGuid().ToString("N"));
        _cacheDir = Path.Combine(root, "cache");
        _workflowDir = Path.Combine(root, "workflows");
        Directory.CreateDirectory(_cacheDir);
        Directory.CreateDirectory(_workflowDir);

        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("http://test-host:8188");

        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _requests.Add(req))
            .ReturnsAsync((HttpRequestMessage _, CancellationToken _) => _response());

        _service = new ComfyUiCheckpointService(
            new StubHttpClientFactory(() => new HttpClient(_handler.Object)),
            _uiState.Object,
            NullLogger<ComfyUiCheckpointService>.Instance,
            cacheDirectoryOverride: _cacheDir,
            workflowsDirectoryOverride: _workflowDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_cacheDir)!, recursive: true); } catch { /* best effort */ }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new()
    {
        StatusCode = status,
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private string CachePath => Path.Combine(_cacheDir, "comfyui-checkpoints.json");

    private void SeedCache(params string[] names) =>
        File.WriteAllText(CachePath, JsonSerializer.Serialize(names));

    // ---- GetCheckpointsAsync --------------------------------------------------------------

    [Fact]
    public async Task GetCheckpoints_RequestsNarrowObjectInfoEndpoint_AndParsesNames()
    {
        var names = await _service.GetCheckpointsAsync();

        names.Should().Equal("a.safetensors", "b.safetensors");
        var request = _requests.Single();
        request.RequestUri!.AbsolutePath.Should().Be("/object_info/CheckpointLoaderSimple",
            "the per-class endpoint avoids the multi-MB full /object_info dump");
        request.RequestUri.Host.Should().Be("test-host", "base URL comes from the UI state store");
    }

    [Fact]
    public async Task GetCheckpoints_OnSuccess_WritesAtomicCacheFile()
    {
        await _service.GetCheckpointsAsync();

        File.Exists(CachePath).Should().BeTrue();
        File.Exists(CachePath + ".tmp").Should().BeFalse("the temp file must be moved, not left behind");
        JsonSerializer.Deserialize<List<string>>(File.ReadAllText(CachePath))
            .Should().Equal("a.safetensors", "b.safetensors");
    }

    [Fact]
    public async Task GetCheckpoints_ServerError_FallsBackToCachedList()
    {
        SeedCache("cached.safetensors");
        _response = () => Json("oops", HttpStatusCode.InternalServerError);

        var names = await _service.GetCheckpointsAsync();

        names.Should().Equal("cached.safetensors");
    }

    [Fact]
    public async Task GetCheckpoints_ConnectionFailure_WithoutCache_ReturnsNull()
    {
        _response = () => throw new HttpRequestException("connection refused");

        var names = await _service.GetCheckpointsAsync();

        names.Should().BeNull();
    }

    [Fact]
    public async Task GetCheckpoints_MalformedObjectInfo_FallsBackToCache()
    {
        SeedCache("cached.safetensors");
        _response = () => Json("""{ "CheckpointLoaderSimple": { "input": { "required": {} } } }""");

        var names = await _service.GetCheckpointsAsync();

        names.Should().Equal("cached.safetensors");
    }

    [Fact]
    public async Task GetCheckpoints_InvalidBaseUrl_ReturnsCacheWithoutHttpCall()
    {
        SeedCache("cached.safetensors");
        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("not a url");

        var names = await _service.GetCheckpointsAsync();

        names.Should().Equal("cached.safetensors");
        _requests.Should().BeEmpty();
    }

    // ---- GetWorkflowCheckpointAsync ---------------------------------------------------------

    [Fact]
    public async Task GetWorkflowCheckpoint_ReadsTemplate_ReturnsBakedName()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "My Workflow.json"),
            """
            {
              "4": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "baked.safetensors" } },
              "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } }
            }
            """);

        var baked = await _service.GetWorkflowCheckpointAsync("My Workflow");

        baked.Should().Be("baked.safetensors");
    }

    [Fact]
    public async Task GetWorkflowCheckpoint_MissingFileOrNoLoader_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "No Loader.json"),
            """{ "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "x" } } }""");

        (await _service.GetWorkflowCheckpointAsync("does-not-exist")).Should().BeNull();
        (await _service.GetWorkflowCheckpointAsync("No Loader")).Should().BeNull();
    }
}
