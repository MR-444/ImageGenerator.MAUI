using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

/// <summary>
/// Talks to a user-operated ComfyUI instance over its built-in HTTP API: loads the workflow
/// template named by the model id, patches it (ComfyUiWorkflowPatcher), queues it via
/// POST /prompt, polls GET /history/{id} until terminal, and downloads the first
/// SaveImage output via GET /view. Create→poll shape mirrors Replicate; error/cancel
/// conventions mirror PollinationsImageGenerationService. No token — the server is the
/// user's own machine; the base URL comes from IUiStateStore per request.
/// </summary>
public sealed class ComfyUiImageGenerationService : IImageGenerationService
{
    internal const string HttpClientName = "comfyui";

    // A real PNG out of a sampler is always tens of KB; anything smaller is an error body.
    private const int MinValidImageBytes = 1000;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);
    // Generous: the GPU run itself is 30-120s, but the server queue may hold earlier jobs.
    private static readonly TimeSpan DefaultMaxPollDuration = TimeSpan.FromMinutes(10);
    // The user's ct is already canceled when the cancel-notify runs, so it gets its own
    // budget — short, because a dead host must not hang the cancel path (the shared
    // resilience pipeline would allow minutes).
    private static readonly TimeSpan CancelNotifyTimeout = TimeSpan.FromSeconds(5);
    // WS connect budget: a proxied/blocked setup must degrade to polling fast, not stall the job.
    private static readonly TimeSpan WsConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IModelDescriptorRegistry _registry;
    private readonly IUiStateStore _uiStateStore;
    private readonly IComfyUiAuthStore _authStore;
    private readonly ILogger<ComfyUiImageGenerationService> _logger;
    private readonly string _workflowsDirectory;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _maxPollDuration;
    private readonly Func<IComfyUiSocket> _socketFactory;

    public ComfyUiImageGenerationService(
        IHttpClientFactory httpClientFactory,
        IModelDescriptorRegistry registry,
        IUiStateStore uiStateStore,
        IComfyUiAuthStore authStore,
        ILogger<ComfyUiImageGenerationService> logger,
        string? workflowsDirectoryOverride = null,
        TimeSpan? pollInterval = null,
        TimeSpan? maxPollDuration = null,
        Func<IComfyUiSocket>? socketFactory = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workflowsDirectory = workflowsDirectoryOverride ?? OutputPaths.ComfyWorkflowsDirectory;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _maxPollDuration = maxPollDuration ?? DefaultMaxPollDuration;
        _socketFactory = socketFactory ?? (() => new ClientWebSocketComfyUiSocket());
    }

    public async Task<GeneratedImage> GenerateImageAsync(
        ImageGenerationParameters parameters,
        CancellationToken cancellationToken = default,
        IProgress<JobProgress>? progress = null)
    {
        try
        {
            if (_registry.PayloadFor(parameters.Model).Build(parameters) is not ComfyUiRequest request)
            {
                _logger.LogError("ComfyUI descriptor mismatch Model={Model}", parameters.Model);
                return Fail($"ComfyUI descriptor for '{parameters.Model}' did not return a ComfyUiRequest payload.");
            }

            var templatePath = Path.Combine(_workflowsDirectory, request.WorkflowName + ".json");
            if (!File.Exists(templatePath))
            {
                return Fail(
                    $"Workflow '{request.WorkflowName}.json' not found in {_workflowsDirectory}. "
                    + "Export it from ComfyUI via Workflow > Export (API).");
            }
            var template = await File.ReadAllTextAsync(templatePath, cancellationToken);

            ComfyUiPatchResult patched;
            try
            {
                patched = ComfyUiWorkflowPatcher.Patch(template, request);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                _logger.LogError(ex, "ComfyUI patch failed Workflow={Workflow}", request.WorkflowName);
                return Fail(ex is JsonException
                    ? $"Workflow '{request.WorkflowName}.json' is not valid JSON: {ex.Message}"
                    : ex.Message);
            }
            _logger.LogInformation(
                "ComfyUI patched Workflow={Workflow} Target={Target} SeedNodes={SeedNodes}",
                request.WorkflowName, patched.PromptTargetDescription, string.Join(",", patched.SeedNodeIds));

            var baseUrl = _uiStateStore.LoadComfyUiBaseUrl() ?? ModelConstants.ComfyUi.DefaultBaseUrl;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                return Fail($"The ComfyUI server URL '{baseUrl}' is not a valid absolute URL — fix it in the ComfyUI server setting.");
            }

            // Re-read per run like the base URL — a header edit applies to the next job
            // without a restart. Empty = LAN setup, no header (Apply no-ops).
            var authHeader = await _authStore.LoadAsync();

            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            ComfyUiAuthHeader.Apply(httpClient, authHeader);

            // The ws connection must exist with the SAME clientId BEFORE the prompt is queued,
            // or the server's early events (execution_start, first progress) are lost.
            var clientId = Guid.NewGuid().ToString("N");
            await using var socket = await TryConnectSocketAsync(baseUri, clientId, authHeader, cancellationToken);

            var promptId = await QueuePromptAsync(httpClient, baseUri, patched.GraphJson, clientId, cancellationToken);
            if (promptId.Error is not null) return Fail(promptId.Error);

            // The HttpClient/Polly per-request logging is blackholed below Warning
            // (CrashLogger) — these two INFO lines carry the run timeline instead.
            _logger.LogInformation("ComfyUI queued PromptId={PromptId}", promptId.Value);

            try
            {
                // Live progress + completion signal; on any ws failure this returns silently
                // and the poll below carries the job exactly as before.
                if (socket is not null)
                {
                    await WaitViaWebSocketAsync(socket, promptId.Value!, progress, cancellationToken);
                }

                // First iteration returns immediately when the ws saw completion; otherwise
                // this is the unchanged 2 s polling fallback.
                var entry = await PollHistoryAsync(httpClient, baseUri, promptId.Value!, cancellationToken);

                if (entry.Status?.StatusStr == "error"
                    || entry.Status is { Completed: false, StatusStr: not null })
                {
                    var detail = ExtractExecutionError(entry.Status);
                    _logger.LogError("ComfyUI execution failed PromptId={PromptId} Detail={Detail}", promptId.Value, detail);
                    return Fail($"ComfyUI workflow failed: {Truncate(detail)}");
                }

                var image = (entry.Outputs?.Values ?? Enumerable.Empty<ComfyUiNodeOutput>())
                    .SelectMany(o => o.Images ?? [])
                    .Where(i => !string.IsNullOrEmpty(i.Filename))
                    // SaveImage emits "output"; PreviewImage emits "temp" — prefer the saved one.
                    .OrderBy(i => i.Type == "output" ? 0 : 1)
                    .FirstOrDefault();
                if (image is null)
                {
                    return Fail("ComfyUI completed but produced no output images — does the workflow contain a SaveImage node?");
                }

                _logger.LogInformation(
                    "ComfyUI finished PromptId={PromptId}, downloading {Filename}", promptId.Value, image.Filename);

                var bytes = await DownloadImageAsync(httpClient, baseUri, image, cancellationToken);
                if (bytes.Length < MinValidImageBytes)
                {
                    _logger.LogError("ComfyUI undersized image Bytes={Bytes} File={File}", bytes.Length, image.Filename);
                    return Fail($"ComfyUI returned an undersized response ({bytes.Length} bytes) for '{image.Filename}'.");
                }

                return new GeneratedImage
                {
                    Message = $"Image generated by ComfyUI workflow '{request.WorkflowName}'.",
                    ImageData = bytes
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The prompt is queued (or rendering) server-side — tell the server to stop
                // instead of letting it finish a render nobody will collect.
                await TryCancelServerJobAsync(httpClient, baseUri, promptId.Value!);
                return Fail("Image generation was canceled.");
            }
        }
        catch (OperationCanceledException)
        {
            // Pre-queue cancellation (template read, POST /prompt): nothing exists server-side
            // yet, so there is nothing to stop. Post-queue cancellation is handled by the
            // nested catch above, which notifies the server first.
            return Fail("Image generation was canceled.");
        }
        catch (TimeoutException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ComfyUiImageGenerationService.GenerateImageAsync threw Model={Model}", parameters.Model);
            return Fail(FormatError(ex));
        }
    }

    private async Task<(string? Value, string? Error)> QueuePromptAsync(
        HttpClient httpClient, Uri baseUri, string graphJson, string clientId, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["prompt"] = JsonNode.Parse(graphJson),
            // Same id the ws connected with — that is what routes the progress events to us.
            ["client_id"] = clientId
        };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(new Uri(baseUri, "prompt"), content, ct);

        var responseBody = await ReadBodySafeAsync(response, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "ComfyUI HTTP {StatusCode} {Status} on POST /prompt Body={Body}",
                (int)response.StatusCode, response.StatusCode, responseBody);
            return (null, $"ComfyUI HTTP {(int)response.StatusCode} {response.StatusCode}: {Truncate(ExtractValidationError(responseBody))}");
        }

        var parsed = TryDeserialize<ComfyUiPromptResponse>(responseBody);
        if (string.IsNullOrEmpty(parsed?.PromptId))
        {
            _logger.LogError("ComfyUI POST /prompt returned no prompt_id Body={Body}", responseBody);
            return (null, $"ComfyUI accepted the request but returned no prompt id: {Truncate(responseBody)}");
        }
        return (parsed.PromptId, null);
    }

    /// <summary>
    /// Opens the progress WebSocket (ws(s)://host/ws?clientId=…) with a short connect budget.
    /// Null on any failure — the caller silently degrades to /history polling, so proxied or
    /// ws-blocked setups keep working.
    /// </summary>
    private async Task<IComfyUiSocket?> TryConnectSocketAsync(
        Uri baseUri, string clientId, string? authHeader, CancellationToken ct)
    {
        var socket = _socketFactory();
        try
        {
            // Resolve "ws" relative to the base like the HTTP endpoints do, then flip the scheme.
            var wsUri = new UriBuilder(new Uri(baseUri, "ws"))
            {
                Scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
                Query = "clientId=" + clientId
            }.Uri;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(WsConnectTimeout);
            await socket.ConnectAsync(wsUri, authHeader, timeoutCts.Token);

            _logger.LogInformation("ComfyUI ws connected ClientId={ClientId}", clientId);
            return socket;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await socket.DisposeAsync();
            throw;
        }
        catch (Exception ex)
        {
            await socket.DisposeAsync();
            _logger.LogWarning(ex, "ComfyUI ws unavailable — falling back to polling");
            return null;
        }
    }

    /// <summary>
    /// Consumes ws events until OUR prompt completes or fails (history carries the outcome
    /// either way — the caller's /history fetch handles both), reporting sampler progress
    /// along the way. Every event is filtered by prompt id: clientId routing should already
    /// isolate us, but a browser tab on the same server must never confuse the app. Returns
    /// normally on any socket failure so the caller falls back to polling.
    /// </summary>
    private async Task WaitViaWebSocketAsync(
        IComfyUiSocket socket, string promptId, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        try
        {
            // Same overall deadline as the poll loop, enforced on the receive calls.
            using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadlineCts.CancelAfter(_maxPollDuration);

            while (true)
            {
                var message = await socket.ReceiveTextAsync(deadlineCts.Token);
                if (message is null)
                {
                    _logger.LogWarning("ComfyUI ws closed by server — falling back to polling");
                    return;
                }

                switch (ComfyUiWsEvent.TryParse(message))
                {
                    case ComfyUiWsEvent.ExecutionStart start when start.PromptId == promptId:
                        progress?.Report(new JobProgress("Rendering…"));
                        break;

                    case ComfyUiWsEvent.Progress step when step.PromptId == promptId:
                        progress?.Report(new JobProgress(
                            $"Rendering… {step.Value}/{step.Max}",
                            step.Max > 0 ? (double)step.Value / step.Max : null));
                        break;

                    case ComfyUiWsEvent.Completed completed when completed.PromptId == promptId:
                        return;

                    case ComfyUiWsEvent.Failed failed when failed.PromptId == promptId:
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // user cancel — the caller's cancel path notifies the server
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(PollTimeoutMessage()); // ws stayed silent past the deadline
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI ws receive failed — falling back to polling");
        }
    }

    private string PollTimeoutMessage() =>
        $"ComfyUI did not finish within {_maxPollDuration.TotalMinutes:0} minutes — "
        + "the job may still be queued behind others on the server.";

    /// <summary>
    /// Best-effort server-side cancel, mirroring what ComfyUI's own UI cancel button does:
    /// POST /queue {"delete":[id]} drops the prompt if it is still pending, then — only when
    /// GET /queue shows OUR prompt as the one currently executing — POST /interrupt stops the
    /// render. The running-check matters because /interrupt is GLOBAL: a blind interrupt
    /// would kill whatever is rendering, including a job the user started from the ComfyUI
    /// browser UI while ours sat in the queue. Never throws — the user already canceled, and
    /// a second error on top of that helps nobody.
    /// </summary>
    private async Task TryCancelServerJobAsync(HttpClient httpClient, Uri baseUri, string promptId)
    {
        try
        {
            using var cts = new CancellationTokenSource(CancelNotifyTimeout);

            var deleteBody = new JsonObject { ["delete"] = new JsonArray(promptId) };
            using var deleteContent = new StringContent(deleteBody.ToJsonString(), Encoding.UTF8, "application/json");
            using var deleteResponse = await httpClient.PostAsync(new Uri(baseUri, "queue"), deleteContent, cts.Token);
            _logger.LogInformation(
                "ComfyUI cancel: dequeue requested PromptId={PromptId} (HTTP {StatusCode})",
                promptId, (int)deleteResponse.StatusCode);

            using var queueResponse = await httpClient.GetAsync(new Uri(baseUri, "queue"), cts.Token);
            queueResponse.EnsureSuccessStatusCode();
            await using var stream = await queueResponse.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

            if (!IsPromptRunning(doc, promptId))
            {
                _logger.LogInformation(
                    "ComfyUI cancel: PromptId={PromptId} is not the running job — nothing to interrupt", promptId);
                return;
            }

            using var interruptResponse = await httpClient.PostAsync(
                new Uri(baseUri, "interrupt"), content: null, cts.Token);
            _logger.LogInformation(
                "ComfyUI cancel: interrupted running PromptId={PromptId} (HTTP {StatusCode})",
                promptId, (int)interruptResponse.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI cancel notify failed PromptId={PromptId}", promptId);
        }
    }

    /// <summary>GET /queue's queue_running entries are arrays whose index [1] is the prompt id.</summary>
    private static bool IsPromptRunning(JsonDocument queueDoc, string promptId) =>
        queueDoc.RootElement.ValueKind == JsonValueKind.Object
        && queueDoc.RootElement.TryGetProperty("queue_running", out var running)
        && running.ValueKind == JsonValueKind.Array
        && running.EnumerateArray().Any(entry =>
            entry.ValueKind == JsonValueKind.Array
            && entry.GetArrayLength() > 1
            && entry[1].ValueKind == JsonValueKind.String
            && entry[1].GetString() == promptId);

    /// <summary>Same loop shape as ReplicateHelper.PollForOutputAsync: the per-request HTTP
    /// resilience bounds each GET; this loop owns the overall deadline.</summary>
    private async Task<ComfyUiHistoryEntry> PollHistoryAsync(
        HttpClient httpClient, Uri baseUri, string promptId, CancellationToken ct)
    {
        var historyUri = new Uri(baseUri, $"history/{Uri.EscapeDataString(promptId)}");
        var deadline = DateTimeOffset.UtcNow + _maxPollDuration;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException(PollTimeoutMessage());
            }

            using var response = await httpClient.GetAsync(historyUri, ct);
            var body = await ReadBodySafeAsync(response, ct);
            if (response.IsSuccessStatusCode)
            {
                var history = TryDeserialize<Dictionary<string, ComfyUiHistoryEntry>>(body);
                if (history is not null && history.TryGetValue(promptId, out var entry))
                {
                    return entry;
                }
            }
            else
            {
                // Transient server hiccup — log and keep polling until the deadline.
                _logger.LogWarning(
                    "ComfyUI HTTP {StatusCode} on GET /history/{PromptId}", (int)response.StatusCode, promptId);
            }

            await Task.Delay(_pollInterval, ct);
        }
    }

    private static async Task<byte[]> DownloadImageAsync(
        HttpClient httpClient, Uri baseUri, ComfyUiImageRef image, CancellationToken ct)
    {
        var viewUri = new Uri(baseUri,
            $"view?filename={Uri.EscapeDataString(image.Filename!)}"
            + $"&subfolder={Uri.EscapeDataString(image.Subfolder ?? string.Empty)}"
            + $"&type={Uri.EscapeDataString(image.Type ?? "output")}");
        using var response = await httpClient.GetAsync(viewUri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>400 bodies carry {"error":{message},"node_errors":{...}} — surface the message
    /// plus the node errors compactly; fall back to the raw body.</summary>
    private static string ExtractValidationError(string body)
    {
        var envelope = TryDeserialize<ComfyUiErrorEnvelope>(body);
        if (envelope?.Error?.Message is not { Length: > 0 } message) return body;

        var nodeErrors = envelope.NodeErrors is { ValueKind: JsonValueKind.Object } ne
                         && ne.EnumerateObject().Any()
            ? $" Node errors: {ne.GetRawText()}"
            : string.Empty;
        return message + nodeErrors;
    }

    private static string ExtractExecutionError(ComfyUiHistoryStatus? status)
    {
        if (status?.Messages is not { ValueKind: JsonValueKind.Array } messages)
        {
            return status?.StatusStr ?? "unknown error";
        }

        // messages = [["execution_error", {exception_message, node_type, ...}], ...] — pick the
        // exception_message when present, otherwise hand back the raw entry.
        foreach (var entry in messages.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2) continue;
            if (entry[0].GetString() != "execution_error") continue;

            var detail = entry[1];
            if (detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("exception_message", out var msg)
                && msg.GetString() is { Length: > 0 } text)
            {
                var nodeType = detail.TryGetProperty("node_type", out var nt) ? nt.GetString() : null;
                return nodeType is null ? text : $"{nodeType}: {text}";
            }
            return detail.GetRawText();
        }
        return status.StatusStr ?? "unknown error";
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return null; }
    }

    private static async Task<string> ReadBodySafeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body) ? "(no body)" : body;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"(body read failed: {ex.Message})";
        }
    }

    private static string Truncate(string text) =>
        text.Length > 500 ? text[..500] + "…" : text;

    private static string FormatError(Exception ex)
    {
        var deepest = ex;
        while (deepest.InnerException != null) deepest = deepest.InnerException;
        return deepest.Message == ex.Message
            ? $"An error occurred: {ex.Message}"
            : $"An error occurred: {ex.Message} ({deepest.Message})";
    }

    private static GeneratedImage Fail(string message) => new() { Message = message, ImageData = null };
}
