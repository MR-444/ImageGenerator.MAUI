using System.Text;
using System.Text.Json;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Infrastructure.Imaging;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Civitai;

/// <summary>
/// Talks to CivitAI over two surfaces with the same Bearer API key (live-verified 2026-06-12):
///
///  - MCP server (https://mcp.civitai.com/mcp): stateless JSON-RPC 2.0 over plain HTTP POST.
///    Used for upload_image (chains presign + PUT server-side — one call instead of the raw
///    orchestrator upload flow) and whoami (Settings "Test connection").
///
///  - tRPC endpoint (https://civitai.com/api/trpc/post.createWithImages): post creation.
///    Called directly — NOT through the MCP create_post tool — because the MCP's input schema
///    strips images[].meta, and structured meta is the only way generation data reaches an
///    API-uploaded image (CivitAI parses A1111 `parameters` chunks client-side in the website
///    upload page only; server-side uploads never read file metadata — proven by experiment).
///
/// Both surfaces are unversioned (MCP self-reports 0.1.0) — response parsing is tolerant and
/// every server-side failure returns an unsuccessful result instead of throwing.
/// </summary>
public sealed class CivitaiPostingService(
    IHttpClientFactory httpClientFactory,
    ICivitaiTokenStore tokenStore,
    ILogger<CivitaiPostingService> logger)
    : ICivitaiPostingService
{
    internal const string HttpClientName = "civitai";

    private const string McpEndpoint = "https://mcp.civitai.com/mcp";
    private const string TrpcBaseUrl = "https://civitai.com/api/trpc/";
    private const string PostUrlPrefix = "https://civitai.com/posts/";

    // upload_image rejects anything whose *decoded* size passes 10 MiB, so oversize files are
    // re-encoded for the upload only: the file on disk is untouched, and the generation data is
    // unaffected because it rides in the tRPC meta field rather than in the image bytes (see the
    // class remarks). Budget, quality and "never resize" live with every other outgoing image in
    // ImagePayloadLimits.CivitAiUpload.
    private static readonly ImagePayloadLimits UploadLimits = ImagePayloadLimits.CivitAiUpload;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ICivitaiTokenStore _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
    private readonly ILogger<CivitaiPostingService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private int _requestId;

    public Task<CivitaiPostResult> PostImageAsync(
        string filePath,
        string title,
        IReadOnlyDictionary<string, object>? meta,
        int? modelVersionId,
        CancellationToken cancellationToken = default)
        // The generation-time path: one image, published one-step (user decision 2026-06-13).
        => PostImagesAsync(
            (CivitaiImagePost[])[new CivitaiImagePost(filePath, meta)], title, modelVersionId,
            publish: true, cancellationToken);

    public async Task<CivitaiPostResult> PostImagesAsync(
        IReadOnlyList<CivitaiImagePost> images,
        string title,
        int? modelVersionId,
        bool publish,
        CancellationToken cancellationToken = default)
    {
        if (images.Count == 0)
            return new CivitaiPostResult(false, null, null, "No images to post.");

        var apiKey = await _tokenStore.LoadAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new CivitaiPostResult(false, null, null,
                "No CivitAI API key — add it on the Settings page.");
        }

        try
        {
            // Upload each image first (byte-identical unless it exceeded the size cap and had to
            // be re-encoded) and collect the post-image payloads; a single create call then
            // groups them into one post.
            var imagePayloads = new List<Dictionary<string, object?>>(images.Count);
            var recompressed = 0;
            for (var index = 0; index < images.Count; index++)
            {
                var (payload, wasRecompressed) = await UploadAndBuildImageAsync(
                    images[index].FilePath, images[index].Meta, index, modelVersionId,
                    apiKey, cancellationToken);
                imagePayloads.Add(payload);
                if (wasRecompressed) recompressed++;
            }

            var input = new Dictionary<string, object?>
            {
                // publish:true = post-and-done (generation-time checkbox); publish:false = draft
                // (Gallery batch flow — the user reviews and publishes on the site).
                ["publish"] = publish,
                ["images"] = imagePayloads,
            };
            // Title is optional server-side; an empty one (e.g. a JSON prompt with no usable
            // description) is better omitted than sent as an empty string.
            if (!string.IsNullOrWhiteSpace(title)) input["title"] = title;
            if (modelVersionId is { } postVersionId) input["modelVersionId"] = postVersionId;

            var created = await CallTrpcAsync("post.createWithImages", input, apiKey, cancellationToken);
            var postId = GetInt(created, "id")
                ?? throw new CivitaiApiException("CivitAI post creation returned no post id (response shape changed?).");

            _logger.LogInformation(
                "CivitAI post {Mode} Id={PostId} Images={Count}",
                publish ? "published" : "drafted", postId, images.Count);
            var outcome = publish ? "Posted to CivitAI" : "Created CivitAI draft";
            var note = recompressed == 0
                ? "."
                : $" ({recompressed} of {images.Count} re-encoded to fit CivitAI's {UploadLimits.MaxBytes / (1024 * 1024)} MiB limit).";
            return new CivitaiPostResult(true, postId, $"{PostUrlPrefix}{postId}", outcome + note);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // CivitaiApiException carries a server-shaped message; everything else (transport,
            // Polly timeout, IO) gets its exception message. Posting is a post-save side
            // effect — it must surface as a status line, never as a thrown error.
            _logger.LogWarning(ex, "CivitAI posting failed Images={Count}", images.Count);
            return new CivitaiPostResult(false, null, null, ex.Message);
        }
    }

    /// <summary>
    /// Uploads one file via MCP upload_image and builds its post.createWithImages image entry
    /// (url=UUID, index, optional width/height/meta/modelVersionId). The modelVersionId rides on
    /// the image AND the post, mirroring the MCP wrapper — this lands the post in the model gallery.
    /// </summary>
    private async Task<(Dictionary<string, object?> Image, bool Recompressed)> UploadAndBuildImageAsync(
        string filePath,
        IReadOnlyDictionary<string, object>? meta,
        int index,
        int? modelVersionId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

        // Shrunk locally rather than letting the server reject it: the upload would otherwise push
        // ~4/3 of an oversize file over the wire before failing. The budget is bytes only — the
        // pixels an upscale existed to produce stay, and transparency stays (see
        // ImagePayloadLimits.CivitAiUpload).
        var prepared = await ImagePayloadNormalizer.PrepareAsync(bytes, UploadLimits, cancellationToken);
        if (prepared.Bytes is null)
        {
            throw new CivitaiApiException($"{Path.GetFileName(filePath)}: {prepared.Error}");
        }

        var recompressed = prepared.Note is not null;
        if (recompressed)
        {
            _logger.LogInformation(
                "CivitAI upload re-encoded File={File} {Change}", Path.GetFileName(filePath), prepared.Note);
        }

        bytes = prepared.Bytes;

        var base64 = Convert.ToBase64String(bytes);
        var contentType = ImageDataUriEncoder.DetectImageMimeType(base64);

        var upload = await CallMcpToolAsync(
            "upload_image",
            new Dictionary<string, object> { ["data"] = base64, ["contentType"] = contentType },
            apiKey, cancellationToken);
        var uuid = GetString(upload, "uuid")
            ?? throw new CivitaiApiException("CivitAI upload returned no image UUID (response shape changed?).");

        var image = new Dictionary<string, object?>
        {
            ["url"] = uuid,
            ["index"] = index,
            ["type"] = "image",
        };
        if (GetInt(upload, "width") is { } width) image["width"] = width;
        if (GetInt(upload, "height") is { } height) image["height"] = height;
        if (meta is { Count: > 0 }) image["meta"] = meta;
        if (modelVersionId is { } versionId) image["modelVersionId"] = versionId;
        return (image, recompressed);
    }

    public async Task<CivitaiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = await _tokenStore.LoadAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new CivitaiConnectionResult(false, "No CivitAI API key saved yet.");
        }

        try
        {
            var payload = await CallMcpToolAsync(
                "whoami", new Dictionary<string, object>(), apiKey, cancellationToken);
            var username = GetString(payload, "username") ?? "(unknown user)";
            var tier = GetString(payload, "tier");
            return new CivitaiConnectionResult(true,
                tier is null ? $"Connected as {username}." : $"Connected as {username} ({tier}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CivitAI whoami failed");
            return new CivitaiConnectionResult(false, ex.Message);
        }
    }

    /// <summary>
    /// One MCP tools/call round-trip. Returns the tool payload: structuredContent when the
    /// server provides it (it does today), else the first content text parsed as JSON.
    /// </summary>
    private async Task<JsonElement> CallMcpToolAsync(
        string tool,
        IReadOnlyDictionary<string, object> arguments,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var envelope = new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Interlocked.Increment(ref _requestId),
            ["method"] = "tools/call",
            ["params"] = new Dictionary<string, object> { ["name"] = tool, ["arguments"] = arguments },
        };

        using var doc = await SendAsync(McpEndpoint, envelope, apiKey, $"CivitAI {tool}", cancellationToken);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var rpcError))
        {
            var message = rpcError.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw new CivitaiApiException($"CivitAI {tool}: {message ?? rpcError.ToString()}");
        }

        if (!root.TryGetProperty("result", out var result))
            throw new CivitaiApiException($"CivitAI {tool}: response had no result (shape changed?).");

        if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            throw new CivitaiApiException($"CivitAI {tool}: {ExtractMcpErrorText(result)}");

        if (result.TryGetProperty("structuredContent", out var structured))
            return structured.Clone();

        if (FirstContentText(result) is { } text)
        {
            try
            {
                using var inner = JsonDocument.Parse(text);
                return inner.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Plain-text payload (e.g. "Uploaded image. UUID: …") with no structured
                // variant — nothing the callers' property lookups could use.
            }
        }

        throw new CivitaiApiException($"CivitAI {tool}: response had no readable payload (shape changed?).");
    }

    /// <summary>
    /// One tRPC mutation round-trip (superjson envelope: request {json: input}, response
    /// result.data.json). Throws CivitaiApiException with the server's message on error.
    /// </summary>
    private async Task<JsonElement> CallTrpcAsync(
        string procedure,
        IReadOnlyDictionary<string, object?> input,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var envelope = new Dictionary<string, object?> { ["json"] = input };

        using var doc = await SendAsync(
            TrpcBaseUrl + procedure, envelope, apiKey, $"CivitAI {procedure}", cancellationToken);
        var root = doc.RootElement;

        if (root.TryGetProperty("result", out var result)
            && result.TryGetProperty("data", out var data))
        {
            return data.TryGetProperty("json", out var json) ? json.Clone() : data.Clone();
        }

        throw new CivitaiApiException($"CivitAI {procedure}: response had no result (shape changed?).");
    }

    private async Task<JsonDocument> SendAsync(
        string url,
        object body,
        string apiKey,
        string operation,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // tRPC errors put the human-readable reason in error.json.message; surface it
            // instead of the bare status line when present. Full body goes to app.log only.
            _logger.LogWarning(
                "{Operation} HTTP {StatusCode} Body={Body}",
                operation, (int)response.StatusCode, Truncate(responseBody, 1000));
            throw new CivitaiApiException(
                $"{operation}: HTTP {(int)response.StatusCode} — {ExtractTrpcErrorMessage(responseBody) ?? Truncate(responseBody, 200)}");
        }

        try
        {
            return JsonDocument.Parse(UnwrapSse(responseBody));
        }
        catch (JsonException)
        {
            throw new CivitaiApiException($"{operation}: response was not JSON (shape changed?).");
        }
    }

    /// <summary>
    /// Defensive: the MCP endpoint returns plain JSON today, but it advertises text/event-stream
    /// support — if a future version streams, take the first data: line.
    /// </summary>
    internal static string UnwrapSse(string body)
    {
        if (!body.StartsWith("event:", StringComparison.Ordinal)
            && !body.StartsWith("data:", StringComparison.Ordinal))
        {
            return body;
        }

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
                return line["data:".Length..].Trim();
        }
        return body;
    }

    private static string ExtractMcpErrorText(JsonElement result)
    {
        if (result.TryGetProperty("structuredContent", out var structured)
            && structured.ValueKind == JsonValueKind.Object
            && structured.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.String)
        {
            return error.GetString()!;
        }
        return FirstContentText(result) ?? "tool returned an error.";
    }

    private static string? FirstContentText(JsonElement result)
    {
        if (result.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    return text.GetString();
            }
        }
        return null;
    }

    private static string? ExtractTrpcErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var inner = error.TryGetProperty("json", out var json) ? json : error;
                if (inner.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
