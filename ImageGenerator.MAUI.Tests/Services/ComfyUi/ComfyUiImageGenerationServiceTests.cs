using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ImageGenerator.MAUI.Tests.Services.ComfyUi;

public sealed class ComfyUiImageGenerationServiceTests : IDisposable
{
    private const string PromptId = "abc-123";

    private const string Template =
        """
        {
          "6":   { "class_type": "CLIPTextEncode", "inputs": { "text": "old" } },
          "165": { "class_type": "RandomNoise", "inputs": { "noise_seed": 1 } },
          "179": { "class_type": "Ideogram4PromptBuilderKJ",
                   "inputs": { "import_json": "", "import_mode": "when empty" } }
        }
        """;

    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Loose);
    private readonly Mock<IUiStateStore> _uiState = new();
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly List<string> _requestBodies = [];
    private readonly string _workflowDir;
    private readonly ComfyUiImageGenerationService _service;

    // Routed per request path; tests override the entries they care about.
    private Func<HttpResponseMessage> _promptResponse = PromptOk;
    private readonly Queue<Func<HttpResponseMessage>> _historyResponses = new();
    private Func<HttpResponseMessage> _viewResponse = ViewOk;

    public ComfyUiImageGenerationServiceTests()
    {
        _workflowDir = Path.Combine(Path.GetTempPath(), "imggen-comfy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workflowDir);
        File.WriteAllText(Path.Combine(_workflowDir, "My Workflow.json"), Template);

        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("http://test-host:8188");

        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                _requests.Add(req);
                _requestBodies.Add(req.Content?.ReadAsStringAsync().Result ?? string.Empty);
            })
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) => Route(req));

        _service = new ComfyUiImageGenerationService(
            new StubHttpClientFactory(() => new HttpClient(_handler.Object)),
            ModelDescriptorRegistry.Default(),
            _uiState.Object,
            NullLogger<ComfyUiImageGenerationService>.Instance,
            workflowsDirectoryOverride: _workflowDir,
            pollInterval: TimeSpan.FromMilliseconds(1),
            maxPollDuration: TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        try { Directory.Delete(_workflowDir, recursive: true); } catch { /* best effort */ }
    }

    private HttpResponseMessage Route(HttpRequestMessage req)
    {
        var path = req.RequestUri!.AbsolutePath;
        if (path == "/prompt") return _promptResponse();
        if (path.StartsWith("/history/")) return _historyResponses.Count > 0 ? _historyResponses.Dequeue()() : HistoryDone();
        if (path == "/view") return _viewResponse();
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage PromptOk() => Json($$"""{ "prompt_id": "{{PromptId}}" }""");

    private static HttpResponseMessage HistoryEmpty() => Json("{}");

    private static HttpResponseMessage HistoryDone() => Json(
        $$"""
        {
          "{{PromptId}}": {
            "outputs": {
              "25":  { "images": [ { "filename": "preview.png", "subfolder": "", "type": "temp" } ] },
              "200": { "images": [ { "filename": "Ideogram4_001.png", "subfolder": "2026-06", "type": "output" } ] }
            },
            "status": { "status_str": "success", "completed": true }
          }
        }
        """);

    private static HttpResponseMessage ViewOk() => new()
    {
        StatusCode = HttpStatusCode.OK,
        Content = new ByteArrayContent(new byte[4096])
    };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new()
    {
        StatusCode = status,
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static ImageGenerationParameters Parameters(bool json = true) => new()
    {
        Model = "comfyui/My Workflow",
        Prompt = json ? """{"high_level_description":"x"}""" : "plain prompt",
        UseJsonPrompt = json,
        Seed = 777
    };

    [Fact]
    public async Task HappyPath_PostsPatchedGraph_PollsAndDownloadsTheOutputImage()
    {
        _historyResponses.Enqueue(HistoryEmpty);   // first poll: still running
        _historyResponses.Enqueue(HistoryDone);

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().NotBeNull().And.HaveCount(4096);
        result.Message.Should().Contain("My Workflow");

        var postBody = JsonNode.Parse(_requestBodies[_requests.FindIndex(r => r.Method == HttpMethod.Post)])!;
        postBody["client_id"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        var graph = postBody["prompt"]!;
        graph["179"]!["inputs"]!["import_json"]!.GetValue<string>().Should().Contain("high_level_description");
        graph["179"]!["inputs"]!["import_mode"]!.GetValue<string>().Should().Be("always");
        graph["165"]!["inputs"]!["noise_seed"]!.GetValue<long>().Should().Be(777);

        var viewRequest = _requests.Single(r => r.RequestUri!.AbsolutePath == "/view");
        viewRequest.RequestUri!.Query.Should().Contain("filename=Ideogram4_001.png",
            "the type=output image must win over the temp preview");
        viewRequest.RequestUri.Query.Should().Contain("type=output");
        viewRequest.RequestUri.Host.Should().Be("test-host", "base URL comes from the UI state store");
    }

    [Fact]
    public async Task Validation400_SurfacesTheNodeErrorMessage()
    {
        _promptResponse = () => Json(
            """{ "error": { "type": "prompt_outputs_failed_validation", "message": "Prompt outputs failed validation" }, "node_errors": { "179": {} } }""",
            HttpStatusCode.BadRequest);

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("400").And.Contain("Prompt outputs failed validation");
    }

    [Fact]
    public async Task ExecutionError_SurfacesTheExceptionMessageFromHistory()
    {
        _historyResponses.Enqueue(() => Json(
            $$"""
            {
              "{{PromptId}}": {
                "outputs": {},
                "status": {
                  "status_str": "error", "completed": false,
                  "messages": [ ["execution_start", {}],
                                ["execution_error", { "node_type": "KSampler", "exception_message": "CUDA out of memory" }] ]
                }
              }
            }
            """));

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("KSampler").And.Contain("CUDA out of memory");
    }

    [Fact]
    public async Task NeverFinishing_TimesOutWithQueueAwareMessage()
    {
        var service = new ComfyUiImageGenerationService(
            new StubHttpClientFactory(() => new HttpClient(_handler.Object)),
            ModelDescriptorRegistry.Default(),
            _uiState.Object,
            NullLogger<ComfyUiImageGenerationService>.Instance,
            workflowsDirectoryOverride: _workflowDir,
            pollInterval: TimeSpan.FromMilliseconds(1),
            maxPollDuration: TimeSpan.FromMilliseconds(20));
        for (var i = 0; i < 1000; i++) _historyResponses.Enqueue(HistoryEmpty);

        var result = await service.GenerateImageAsync(Parameters());

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("queued");
    }

    [Fact]
    public async Task CanceledMidPoll_ReturnsCanceledMessage()
    {
        using var cts = new CancellationTokenSource();
        _historyResponses.Enqueue(() => { cts.Cancel(); return HistoryEmpty(); });
        for (var i = 0; i < 10; i++) _historyResponses.Enqueue(HistoryEmpty);

        var result = await _service.GenerateImageAsync(Parameters(), cts.Token);

        result.ImageData.Should().BeNull();
        result.Message.Should().Be("Image generation was canceled.");
    }

    [Fact]
    public async Task MissingWorkflowFile_NamesTheDirectoryAndExportHint()
    {
        var parameters = Parameters() ;
        parameters.Model = "comfyui/does-not-exist";

        var result = await _service.GenerateImageAsync(parameters);

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("does-not-exist.json")
            .And.Contain(_workflowDir)
            .And.Contain("Export (API)");
        _requests.Should().BeEmpty("no HTTP traffic before the template loads");
    }

    [Fact]
    public async Task PatcherRejection_BecomesTheJobMessage_WithoutHttpTraffic()
    {
        File.WriteAllText(Path.Combine(_workflowDir, "ui-format.json"),
            """{ "nodes": [ { "id": 1 } ] }""");
        var parameters = Parameters();
        parameters.Model = "comfyui/ui-format";

        var result = await _service.GenerateImageAsync(parameters);

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("Export (API)");
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NoOutputImages_ExplainsSaveImageRequirement()
    {
        _historyResponses.Enqueue(() => Json(
            $$"""{ "{{PromptId}}": { "outputs": {}, "status": { "status_str": "success", "completed": true } } }"""));

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("SaveImage");
    }

    [Fact]
    public async Task UndersizedImage_IsRejected()
    {
        _viewResponse = () => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new ByteArrayContent(new byte[10])
        };

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("undersized");
    }

    [Fact]
    public async Task NullStoreValue_FallsBackToTheDefaultBaseUrl()
    {
        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns((string?)null);

        await _service.GenerateImageAsync(Parameters());

        _requests.Should().NotBeEmpty();
        _requests[0].RequestUri!.Port.Should().Be(8188);
        _requests[0].RequestUri!.Host.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task MalformedBaseUrl_FailsWithSettingGuidance()
    {
        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("not a url");

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("not a valid absolute URL");
        _requests.Should().BeEmpty();
    }
}
