using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Ollama;

/// <summary>
/// A raw <see cref="HttpClient"/> transport against a local Ollama server's NATIVE <c>/api/chat</c>
/// endpoint (default the user's fireEngine box). It mirrors <see cref="AnthropicMessagesTransport"/>'s
/// seam — same <see cref="ChatTurn"/> input, same "return the model's raw text" contract — so the caption
/// mutator can route to it for a FREE technical round-trip (HTTP → structured output → validator/retry →
/// batch) without paying Anthropic. Output quality is not a goal of this path; correctness of the plumbing
/// is. The native endpoint (not the OpenAI-compatible one) is used deliberately: only it exposes
/// <c>think</c> and <c>options.num_ctx</c>, the two knobs that keep a small local model inside the
/// per-attempt timeout. Thinking is DISABLED (a reasoning trace balloons one caption past the timeout) and
/// the context window is pinned (see <see cref="NumCtx"/>) so the validator-feedback retry — which appends
/// the bad output + errors — can't overflow Ollama's 4096 default and silently truncate the system prompt.
/// </summary>
internal static class OllamaChatTransport
{
    public const string HttpClientName = "ollama";

    /// <summary>Context window pinned per call. Sized to hold the system prompt + base caption + schema and
    /// the larger retry turn with headroom; well under the models' trained max, modest extra KV-cache VRAM.</summary>
    private const int NumCtx = 8192;

    /// <summary>Sampling loosened (above Ollama's 0.8 default) so the N independent Local-tier variants
    /// diverge more — with thinking off the model is otherwise conservative and variants look alike.</summary>
    private const double Temperature = 1.0;
    private const double TopP = 0.95;

    private static readonly JsonSerializerOptions BodyJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// One <c>/api/chat</c> call against <paramref name="baseUrl"/>. The system prompt is sent as a leading
    /// <c>system</c> message. When <paramref name="schema"/> is non-null the call requests that JSON schema
    /// as the native <c>format</c>; the message content is returned (markdown fences stripped). Ollama
    /// ignores auth, so no key is sent. Thinking is off and <see cref="NumCtx"/> is pinned. Throws
    /// <see cref="HttpRequestException"/> (carrying the <see cref="System.Net.HttpStatusCode"/>) on a non-2xx
    /// status, or <see cref="InvalidOperationException"/> on a 2xx response carrying no message content.
    /// </summary>
    public static async Task<string> SendAsync(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        string baseUrl,
        string modelId,
        string systemPrompt,
        IReadOnlyList<ChatTurn> messages,
        JsonElement? schema,
        CancellationToken ct)
    {
        var allTurns = new List<object> { new { role = "system", content = systemPrompt } };
        allTurns.AddRange(messages.Select(m => new { role = m.Role, content = m.Content }));

        var body = new
        {
            model = modelId,
            messages = allTurns,
            stream = false,
            // Disable reasoning: a thinking trace is minutes of generation on a local model and trips the
            // per-attempt timeout. This path verifies plumbing, not prose quality — thinking buys nothing here.
            think = false,
            // Native structured output: the JSON schema object goes straight into `format`.
            format = schema is { } s ? (object)s : null,
            // Pin the context so the bigger retry turn can't overflow Ollama's 4096 default and truncate;
            // loosen sampling so the independent variants diverge more.
            options = new { num_ctx = NumCtx, temperature = Temperature, top_p = TopP }
        };

        var endpoint = $"{baseUrl.TrimEnd('/')}/api/chat";
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Ollama HTTP {StatusCode} Url={Url} Body={Body}",
                (int)response.StatusCode, endpoint, Truncate(responseBody, 1000));
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} — {ExtractErrorMessage(responseBody) ?? Truncate(responseBody, 200)}",
                inner: null,
                statusCode: response.StatusCode);
        }

        return ExtractContent(responseBody);
    }

    /// <summary>Pull <c>message.content</c> out of a native <c>/api/chat</c> response.</summary>
    private static string ExtractContent(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return StripJsonFences(content.GetString() ?? string.Empty);
        }

        throw new InvalidOperationException("Ollama response carried no message content.");
    }

    /// <summary>Some local models wrap structured output in a <c>```json … ```</c> markdown fence even with
    /// a schema set; strip a leading/trailing fence so the JSON parses. (The Anthropic path never fences.)</summary>
    private static string StripJsonFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline >= 0)
            trimmed = trimmed[(firstNewline + 1)..];   // drop the opening ``` / ```json line
        if (trimmed.EndsWith("```", StringComparison.Ordinal))
            trimmed = trimmed[..^3];

        return trimmed.Trim();
    }

    private static string? ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            // OpenAI-compat: { "error": { "message": ... } }; Ollama native: { "error": "..." }.
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                return error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : error.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — caller falls back to a truncated raw snippet.
        }

        return null;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";
}
