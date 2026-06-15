using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Anthropic;

/// <summary>One turn in a Messages conversation. Shared by every Anthropic-backed service.</summary>
internal readonly record struct ChatTurn(string Role, string Content);

/// <summary>
/// The raw <see cref="HttpClient"/> + System.Text.Json transport against api.anthropic.com's Messages
/// API, with native structured outputs (<c>output_config.format = json_schema</c>, GA — no beta header).
/// Extracted from <see cref="AnthropicPromptBuilderService"/> so the prompt builder and the caption
/// mutator share one body shape; <paramref name="modelId"/> is a parameter (Sonnet 4.6 / Opus 4.8), no
/// longer hardcoded. The official <c>Anthropic</c> NuGet SDK can replace this later without touching the
/// callers — once its publish behaviour is verified on the trimmed win-x64 single-file target.
/// </summary>
internal static class AnthropicMessagesTransport
{
    public const string HttpClientName = "anthropic";

    private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxOutputTokens = 16000;

    // Verbatim property names (no naming policy): the body uses the exact API field spellings.
    private static readonly JsonSerializerOptions BodyJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// One Messages call: assemble the body, POST it, return the model's first text block. When
    /// <paramref name="schema"/> is non-null the call is constrained to that JSON schema (structured
    /// outputs); when null it's a plain text call. Throws <see cref="InvalidOperationException"/> on a
    /// non-2xx status or a response carrying no text block.
    /// </summary>
    public static async Task<string> SendAsync(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        string modelId,
        string apiKey,
        string systemPrompt,
        IReadOnlyList<ChatTurn> messages,
        JsonElement? schema,
        CancellationToken ct)
    {
        var body = new
        {
            model = modelId,
            max_tokens = MaxOutputTokens,
            // Opus 4.8 / Sonnet 4.6 support adaptive thinking with the same request shape.
            thinking = new { type = "adaptive" },
            // System as a cached block — prompt caching cuts repeat-call input cost ~10×.
            system = new object[]
            {
                new { type = "text", text = systemPrompt, cache_control = new { type = "ephemeral" } }
            },
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            // effort lives INSIDE output_config (per the claude-api skill); high = max creativity. The
            // json_schema format is present ONLY when a schema is supplied (BodyJson drops the null),
            // leaving a schema-less call free to emit prose.
            output_config = new
            {
                effort = "high",
                format = schema is { } s ? (object)new { type = "json_schema", schema = s } : null
            }
        };

        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Anthropic HTTP {StatusCode} Body={Body}",
                (int)response.StatusCode, Truncate(responseBody, 1000));
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode} — {ExtractErrorMessage(responseBody) ?? Truncate(responseBody, 200)}");
        }

        return ExtractText(responseBody);
    }

    /// <summary>Pull the first text content block out of a Messages response (a thinking block may
    /// precede it).</summary>
    private static string ExtractText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        throw new InvalidOperationException("Anthropic response carried no text content block.");
    }

    private static string? ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString();
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
