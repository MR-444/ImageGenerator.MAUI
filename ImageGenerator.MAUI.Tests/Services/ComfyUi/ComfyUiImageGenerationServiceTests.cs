using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging;
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
          "4":   { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "baked.safetensors" } },
          "6":   { "class_type": "CLIPTextEncode", "inputs": { "text": "old" } },
          "10":  { "class_type": "ModelConsumer", "inputs": { "model": ["4", 0] } },
          "165": { "class_type": "RandomNoise", "inputs": { "noise_seed": 1 } },
          "179": { "class_type": "Ideogram4PromptBuilderKJ",
                   "inputs": { "import_json": "", "import_mode": "when empty" } }
        }
        """;

    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Loose);
    private readonly Mock<IUiStateStore> _uiState = new();
    private readonly Mock<IComfyUiAuthStore> _authStore = new();
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly List<string> _requestBodies = [];
    private readonly ListLogger<ComfyUiImageGenerationService> _logger = new();
    private readonly string _workflowDir;
    private readonly ComfyUiImageGenerationService _service;

    // Routed per request path; tests override the entries they care about.
    private Func<HttpResponseMessage> _promptResponse = PromptOk;
    private readonly Queue<Func<HttpResponseMessage>> _historyResponses = new();
    private Func<HttpResponseMessage> _viewResponse = ViewOk;
    private Func<HttpResponseMessage> _queueResponse = QueueEmpty;
    private Func<HttpResponseMessage> _queueDeleteResponse = () => Json("{}");

    public ComfyUiImageGenerationServiceTests()
    {
        _workflowDir = Path.Combine(Path.GetTempPath(), "imggen-comfy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workflowDir);
        File.WriteAllText(Path.Combine(_workflowDir, "My Workflow.json"), Template);
        File.WriteAllText(Path.Combine(_workflowDir, "Unet Workflow.json"),
            """
            {
              "10": { "class_type": "UNETLoader", "inputs": { "unet_name": "flux-dev.safetensors" } },
              "6":  { "class_type": "CLIPTextEncode", "inputs": { "text": "old" } },
              "3":  { "class_type": "KSampler", "inputs": { "seed": 1 } }
            }
            """);

        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("http://test-host:8188");
        _authStore.Setup(s => s.LoadAsync()).ReturnsAsync((string?)null);

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
            _authStore.Object,
            _logger,
            workflowsDirectoryOverride: _workflowDir,
            pollInterval: TimeSpan.FromMilliseconds(1),
            maxPollDuration: TimeSpan.FromSeconds(5),
            socketFactory: () => _socket);
    }

    // Default = ws unavailable, so every non-ws test keeps exercising the polling path (and
    // never attempts a real TCP connect to test-host). Ws tests swap in a scripted fake.
    private FakeComfyUiSocket _socket = new() { FailConnect = true };

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class FakeComfyUiSocket : IComfyUiSocket
    {
        private readonly Queue<string?> _messages = new();

        public bool FailConnect { get; init; }
        public Uri? ConnectedUri { get; private set; }
        public string? ConnectedAuthHeader { get; private set; }

        /// <summary>A null entry simulates the server closing the connection.</summary>
        public void Enqueue(params string?[] messages)
        {
            foreach (var m in messages) _messages.Enqueue(m);
        }

        public Task ConnectAsync(Uri uri, string? authorizationHeader, CancellationToken ct)
        {
            if (FailConnect) throw new InvalidOperationException("ws disabled for this test");
            ConnectedUri = uri;
            ConnectedAuthHeader = authorizationHeader;
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken ct)
        {
            if (_messages.Count > 0) return _messages.Dequeue();
            // Drained: behave like a silent socket — block until the caller's ct fires.
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Progress&lt;T&gt; posts via SynchronizationContext (racy in tests); this reports inline.</summary>
    private sealed class ImmediateProgress : IProgress<ImageGenerator.MAUI.Core.Domain.ValueObjects.JobProgress>
    {
        public List<ImageGenerator.MAUI.Core.Domain.ValueObjects.JobProgress> Reports { get; } = [];
        public void Report(ImageGenerator.MAUI.Core.Domain.ValueObjects.JobProgress value) => Reports.Add(value);
    }

    private static string WsProgress(string promptId, int value, int max) =>
        $$"""{ "type": "progress", "data": { "value": {{value}}, "max": {{max}}, "prompt_id": "{{promptId}}" } }""";

    private static string WsCompleted(string promptId) =>
        $$"""{ "type": "executing", "data": { "node": null, "prompt_id": "{{promptId}}" } }""";

    private static string WsFailed(string promptId) =>
        $$"""{ "type": "execution_error", "data": { "prompt_id": "{{promptId}}" } }""";

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
        if (path == "/queue") return req.Method == HttpMethod.Get ? _queueResponse() : _queueDeleteResponse();
        if (path == "/interrupt") return new HttpResponseMessage(HttpStatusCode.OK);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage QueueEmpty() =>
        Json("""{ "queue_running": [], "queue_pending": [] }""");

    private static HttpResponseMessage QueueWithRunning(string promptId) => Json(
        $$"""{ "queue_running": [[0, "{{promptId}}", {}, {}, []]], "queue_pending": [] }""");

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

    private static ImageGenerationParameters Parameters(bool json = true, string model = "comfyui/My Workflow") => new()
    {
        Model = model,
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
        graph["4"]!["inputs"]!["ckpt_name"]!.GetValue<string>().Should().Be("baked.safetensors",
            "an empty ComfyUiCheckpoint parameter must never patch the loader");

        var viewRequest = _requests.Single(r => r.RequestUri!.AbsolutePath == "/view");
        viewRequest.RequestUri!.Query.Should().Contain("filename=Ideogram4_001.png",
            "the type=output image must win over the temp preview");
        viewRequest.RequestUri.Query.Should().Contain("type=output");
        viewRequest.RequestUri.Host.Should().Be("test-host", "base URL comes from the UI state store");
        _logger.Messages.Should().Contain(message =>
            message.Contains("ComfyUI benchmark")
            && message.Contains("SageRequested=False")
            && message.Contains("SageApplied=False")
            && message.Contains("CoveredLoaderCount=0")
            && message.Contains("Outcome=success")
            && message.Contains("QueueToCompleteMs="));
    }

    [Fact]
    public async Task SageAttention_Enabled_PostsRuntimePatchAndLogsCoveredLoader()
    {
        _uiState.Setup(s => s.LoadUseSageAttention()).Returns(true);

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().NotBeNull();
        var promptRequestIndex = _requests.FindIndex(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/prompt");
        var graph = JsonNode.Parse(_requestBodies[promptRequestIndex])!["prompt"]!.AsObject();
        var sage = graph.Single(node =>
            node.Value?["class_type"]?.GetValue<string>() == "PathchSageAttentionKJ");

        sage.Value!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be("4");
        sage.Value["inputs"]!["sage_attention"]!.GetValue<string>().Should().Be("auto");
        graph["10"]!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be(sage.Key);
        _logger.Messages.Should().Contain(message =>
            message.Contains("ComfyUI benchmark")
            && message.Contains("SageRequested=True")
            && message.Contains("SageApplied=True")
            && message.Contains("CoveredLoaderCount=1")
            && message.Contains("Outcome=success"));
    }

    [Fact]
    public async Task SageAttention_ExecutionFailure_LogsTaggedErrorWithoutFallback()
    {
        _uiState.Setup(s => s.LoadUseSageAttention()).Returns(true);
        _historyResponses.Enqueue(() => Json(
            $$"""
            {
              "{{PromptId}}": {
                "outputs": {},
                "status": {
                  "status_str": "error", "completed": false,
                  "messages": [ ["execution_error", {
                    "node_type": "PathchSageAttentionKJ",
                    "exception_message": "No module named sageattention" } ] ]
                }
              }
            }
            """));

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("sageattention");
        _requests.Count(r => r.RequestUri!.AbsolutePath == "/prompt").Should().Be(1,
            "a Sage runtime error must not trigger an unpatched retry");
        _logger.Messages.Should().Contain(message =>
            message.Contains("ComfyUI benchmark")
            && message.Contains("SageRequested=True")
            && message.Contains("SageApplied=True")
            && message.Contains("CoveredLoaderCount=1")
            && message.Contains("Outcome=error"));
    }

    [Fact]
    public async Task Generate_WithCheckpointParameter_PostsGraphWithPatchedCkptName()
    {
        var parameters = Parameters();
        parameters.ComfyUiCheckpoint = "server.safetensors";

        await _service.GenerateImageAsync(parameters);

        var postBody = JsonNode.Parse(_requestBodies[_requests.FindIndex(r => r.Method == HttpMethod.Post)])!;
        postBody["prompt"]!["4"]!["inputs"]!["ckpt_name"]!.GetValue<string>()
            .Should().Be("server.safetensors");
    }

    [Fact]
    public async Task Generate_SingleUnetWorkflowWithModelPick_PostsGraphWithPatchedUnetName()
    {
        var parameters = Parameters(json: false, model: "comfyui/Unet Workflow");
        parameters.ComfyUiCheckpoint = "other-model.safetensors";

        await _service.GenerateImageAsync(parameters);

        var postBody = JsonNode.Parse(_requestBodies[_requests.FindIndex(r => r.Method == HttpMethod.Post)])!;
        postBody["prompt"]!["10"]!["inputs"]!["unet_name"]!.GetValue<string>()
            .Should().Be("other-model.safetensors");
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
            _authStore.Object,
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

    // ---- server-side cancel (POST /queue delete + guarded POST /interrupt) ----------------
    // Mirrors ComfyUI's own UI cancel. /interrupt is GLOBAL, so it is only sent when GET
    // /queue shows OUR prompt as the running one — a blind interrupt would kill a render the
    // user started from the ComfyUI browser UI.

    private async Task<GeneratedImage> CancelMidPollAsync()
    {
        using var cts = new CancellationTokenSource();
        _historyResponses.Enqueue(() => { cts.Cancel(); return HistoryEmpty(); });
        for (var i = 0; i < 10; i++) _historyResponses.Enqueue(HistoryEmpty);
        return await _service.GenerateImageAsync(Parameters(), cts.Token);
    }

    [Fact]
    public async Task CanceledMidPoll_DeletesPromptFromServerQueue()
    {
        var result = await CancelMidPollAsync();

        result.Message.Should().Be("Image generation was canceled.");
        var deleteIndex = _requests.FindIndex(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/queue");
        deleteIndex.Should().BeGreaterThan(-1, "cancel must ask the server to drop the pending prompt");
        _requestBodies[deleteIndex].Should().Contain("delete").And.Contain(PromptId);
    }

    [Fact]
    public async Task CanceledMidPoll_OurPromptRunning_PostsInterrupt()
    {
        _queueResponse = () => QueueWithRunning(PromptId);

        await CancelMidPollAsync();

        _requests.Should().Contain(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/interrupt");
    }

    [Fact]
    public async Task CanceledMidPoll_AnotherPromptRunning_DoesNotInterrupt()
    {
        _queueResponse = () => QueueWithRunning("someone-elses-render");

        await CancelMidPollAsync();

        _requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == "/interrupt",
            "a foreign running prompt must never be interrupted — /interrupt is global");
    }

    [Fact]
    public async Task CanceledMidPoll_CancelEndpointsUnreachable_StillReturnsCanceledMessage()
    {
        _queueDeleteResponse = () => throw new HttpRequestException("connection refused");

        var result = await CancelMidPollAsync();

        result.ImageData.Should().BeNull();
        result.Message.Should().Be("Image generation was canceled.",
            "the cancel notify is best-effort and must not surface a second error");
    }

    [Fact]
    public async Task CanceledBeforeQueue_NoServerCancelCalls()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _service.GenerateImageAsync(Parameters(), cts.Token);

        result.Message.Should().Be("Image generation was canceled.");
        _requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == "/queue");
        _requests.Should().NotContain(r => r.RequestUri!.AbsolutePath == "/interrupt");
    }

    // ---- WebSocket progress (ws://host/ws?clientId=…) -------------------------------------
    // The ws is the progress + completion signal; outputs still come from /history (which the
    // poll loop fetches exactly once when the ws already saw completion). Any ws failure
    // degrades silently to the 2 s polling pinned by the older tests above.

    private int HistoryRequestCount => _requests.Count(r => r.RequestUri!.AbsolutePath.StartsWith("/history/"));

    [Fact]
    public async Task Ws_HappyPath_ReportsProgressAndFetchesHistoryOnce()
    {
        _socket = new FakeComfyUiSocket();
        _socket.Enqueue(WsProgress(PromptId, 5, 20), WsCompleted(PromptId));
        var progress = new ImmediateProgress();

        var result = await _service.GenerateImageAsync(Parameters(), progress: progress);

        result.ImageData.Should().NotBeNull();
        HistoryRequestCount.Should().Be(1, "the ws already saw completion — no polling needed");
        progress.Reports.Should().ContainSingle(p => p.Message == "Rendering… 5/20" && p.Percent == 0.25);

        // The ws clientId is what routes events to us — it must match POST /prompt's client_id.
        _socket.ConnectedUri.Should().NotBeNull();
        _socket.ConnectedUri!.Scheme.Should().Be("ws");
        _socket.ConnectedUri.Host.Should().Be("test-host");
        _socket.ConnectedUri.AbsolutePath.Should().Be("/ws");
        var postBody = JsonNode.Parse(_requestBodies[_requests.FindIndex(r => r.Method == HttpMethod.Post)])!;
        _socket.ConnectedUri.Query.Should().Be("?clientId=" + postBody["client_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Ws_ForeignPromptEvents_AreIgnored()
    {
        _socket = new FakeComfyUiSocket();
        _socket.Enqueue(
            WsCompleted("someone-elses-render"),
            WsProgress("someone-elses-render", 9, 10),
            WsCompleted(PromptId));
        var progress = new ImmediateProgress();

        var result = await _service.GenerateImageAsync(Parameters(), progress: progress);

        result.ImageData.Should().NotBeNull();
        progress.Reports.Should().BeEmpty("foreign progress must never reach our job card");
    }

    [Fact]
    public async Task Ws_ConnectFails_FallsBackToPolling()
    {
        // Fixture default socket refuses to connect.
        _historyResponses.Enqueue(HistoryEmpty);
        _historyResponses.Enqueue(HistoryDone);

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().NotBeNull();
        HistoryRequestCount.Should().BeGreaterThanOrEqualTo(2, "polling carried the job");
    }

    [Fact]
    public async Task Ws_ClosesMidRun_FallsBackToPolling()
    {
        _socket = new FakeComfyUiSocket();
        _socket.Enqueue(WsProgress(PromptId, 5, 20), null); // server closes after one report
        _historyResponses.Enqueue(HistoryEmpty);
        _historyResponses.Enqueue(HistoryDone);

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().NotBeNull();
        HistoryRequestCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Ws_ExecutionError_SurfacesHistoryErrorDetail()
    {
        _socket = new FakeComfyUiSocket();
        _socket.Enqueue(WsFailed(PromptId));
        _historyResponses.Enqueue(() => Json(
            $$"""
            {
              "{{PromptId}}": {
                "outputs": {},
                "status": {
                  "status_str": "error", "completed": false,
                  "messages": [ ["execution_error", { "node_type": "KSampler", "exception_message": "CUDA out of memory" }] ]
                }
              }
            }
            """));

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("CUDA out of memory");
    }

    [Fact]
    public async Task Ws_CanceledDuringReceive_StillNotifiesServerCancel()
    {
        _socket = new FakeComfyUiSocket(); // no messages — receive blocks until ct fires
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var result = await _service.GenerateImageAsync(Parameters(), cts.Token);

        result.Message.Should().Be("Image generation was canceled.");
        _requests.Should().Contain(r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/queue",
            "cancel during the ws wait must still tell the server to drop the prompt");
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

    // ---- optional Authorization header (proxied setups) ------------------------------------
    // One stored value ("Bearer …", "Basic …") applied to the per-run client's default
    // headers, so every endpoint — including the best-effort cancel path — carries it.
    // Empty/unset = the LAN default: no header anywhere.

    private static bool HasAuth(HttpRequestMessage r, string expected) =>
        r.Headers.TryGetValues("Authorization", out var values) && values.Single() == expected;

    [Fact]
    public async Task AuthHeaderSet_EveryHttpRequestCarriesIt()
    {
        _authStore.Setup(s => s.LoadAsync()).ReturnsAsync("Bearer abc123");
        _historyResponses.Enqueue(HistoryEmpty);
        _historyResponses.Enqueue(HistoryDone);

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().NotBeNull();
        _requests.Should().NotBeEmpty()
            .And.OnlyContain(r => HasAuth(r, "Bearer abc123"),
                "/prompt, /history and /view all reuse the one per-run client");
    }

    [Fact]
    public async Task AuthHeaderSet_CancelPathRequestsCarryItToo()
    {
        _authStore.Setup(s => s.LoadAsync()).ReturnsAsync("Bearer abc123");
        _queueResponse = () => QueueWithRunning(PromptId);

        await CancelMidPollAsync();

        var cancelPaths = new[] { "/queue", "/interrupt" };
        var cancelRequests = _requests.Where(r => cancelPaths.Contains(r.RequestUri!.AbsolutePath)).ToList();
        cancelRequests.Should().NotBeEmpty()
            .And.OnlyContain(r => HasAuth(r, "Bearer abc123"),
                "an auth proxy would otherwise reject the dequeue/interrupt and the render would run on");
    }

    [Fact]
    public async Task AuthHeaderUnset_NoRequestCarriesIt()
    {
        await _service.GenerateImageAsync(Parameters());

        _requests.Should().NotBeEmpty()
            .And.OnlyContain(r => !r.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task AuthHeaderWhitespace_TreatedAsUnset()
    {
        _authStore.Setup(s => s.LoadAsync()).ReturnsAsync("   ");

        await _service.GenerateImageAsync(Parameters());

        _requests.Should().NotBeEmpty()
            .And.OnlyContain(r => !r.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task AuthHeaderSet_ReachesTheWebSocketConnect()
    {
        _authStore.Setup(s => s.LoadAsync()).ReturnsAsync("Bearer abc123");
        _socket = new FakeComfyUiSocket();
        _socket.Enqueue(WsCompleted(PromptId));

        var result = await _service.GenerateImageAsync(Parameters());

        result.ImageData.Should().NotBeNull();
        _socket.ConnectedAuthHeader.Should().Be("Bearer abc123");
    }

    [Fact]
    public async Task AuthHeaderUnset_WebSocketConnectGetsNull()
    {
        _socket = new FakeComfyUiSocket();
        _socket.Enqueue(WsCompleted(PromptId));

        await _service.GenerateImageAsync(Parameters());

        _socket.ConnectedAuthHeader.Should().BeNull();
    }
}
