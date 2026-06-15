using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Ollama;

/// <summary>
/// A raw <see cref="HttpClient"/> transport against a local Ollama server's OpenAI-compatible
/// <c>/v1/chat/completions</c> endpoint (default the user's fireEngine box). It mirrors
/// <see cref="AnthropicMessagesTransport"/>'s seam — same <see cref="ChatTurn"/> input, same "return the
/// model's raw text" contract — so the caption mutator can route to it for a FREE technical round-trip
/// (HTTP → structured output → validator/retry → batch) without paying Anthropic. Output quality is not a
/// goal of this path; correctness of the plumbing is. Structured output uses the OpenAI
/// <c>response_format: json_schema</c> contract, which recent Ollama honours.
/// </summary>
internal static class OllamaChatTransport
{
    public const string HttpClientName = "ollama";

    private static readonly JsonSerializerOptions BodyJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// One chat-completions call against <paramref name="baseUrl"/>. The system prompt is sent as a
    /// leading <c>system</c> message. When <paramref name="schema"/> is non-null the call requests a
    /// json_schema-constrained response; the first choice's message content is returned. Ollama ignores
    /// auth, so no key is sent. Throws <see cref="InvalidOperationException"/> on a non-2xx status or an
    /// empty response.
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
            response_format = schema is { } s
                ? (object)new { type = "json_schema", json_schema = new { name = "v4_prompt", schema = s, strict = true } }
                : null
        };

        var endpoint = $"{baseUrl.TrimEnd('/')}/v1/chat/completions";
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
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode} — {ExtractErrorMessage(responseBody) ?? Truncate(responseBody, 200)}");
        }

        return ExtractContent(responseBody);
    }

    /// <summary>Pull <c>choices[0].message.content</c> out of an OpenAI-style chat response.</summary>
    private static string ExtractContent(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("Ollama response carried no message content.");
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
