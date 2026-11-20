using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.OpenRouter;

/// <summary>
/// A raw <see cref="HttpClient"/> transport against openrouter.ai's Chat Completions API, in the same
/// seam shape as <see cref="AnthropicMessagesTransport"/> and <c>OllamaChatTransport</c> — same
/// <see cref="ChatTurn"/> input, same "hand back the model's raw text" contract — so a text role can be
/// pointed at a catalog of hundreds of models without the calling service knowing the difference.
/// </summary>
/// <remarks>
/// Deliberate choices worth knowing before editing:
/// <list type="bullet">
/// <item><description><c>response_format.json_schema.strict</c> is <b>false</b>. OpenAI's strict mode
/// demands every key be listed in <c>required</c> and every object carry <c>additionalProperties:false</c>,
/// and the V4 caption schema has optional fields — a strict request would be rejected outright by
/// OpenAI-backed endpoints. A non-strict request is honoured where the endpoint can enforce it and
/// ignored where it cannot, and the caller's validator + one-feedback-retry loop is what actually
/// guarantees a usable caption on every model. The alternative (<c>provider.require_parameters</c>)
/// turns "this endpoint cannot do structured output" into a 404, which is a worse answer for a picker
/// that lists models by capability rather than by endpoint.</description></item>
/// <item><description>Replies are fence-stripped (see <see cref="ModelReplyText"/>) because ignoring a
/// schema is a documented, non-erroring outcome on OpenRouter.</description></item>
/// <item><description><c>max_completion_tokens</c>, not the deprecated <c>max_tokens</c>; OpenRouter
/// normalises it across providers.</description></item>
/// </list>
/// </remarks>
internal static class OpenRouterChatTransport
{
    public const string HttpClientName = "openrouter";

    private const string ChatEndpoint = "https://openrouter.ai/api/v1/chat/completions";
    private const int MaxOutputTokens = 16000;

    private static readonly JsonSerializerOptions BodyJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// One Chat Completions call. The system prompt goes in as a leading <c>system</c> message; when
    /// <paramref name="schema"/> is non-null the reply is asked for as that JSON schema. Throws
    /// <see cref="HttpRequestException"/> (carrying the <see cref="System.Net.HttpStatusCode"/>) on a
    /// non-2xx status, or <see cref="InvalidOperationException"/> on a 2xx response with no message content.
    /// </summary>
    public static async Task<string> SendAsync(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        string apiKey,
        string modelId,
        string systemPrompt,
        IReadOnlyList<ChatTurn> messages,
        JsonElement? schema,
        CancellationToken ct)
    {
        // Built as a list rather than a collection expression: the spread in an array literal needs a
        // countable source, and the caller hands over an IReadOnlyList of turns we are reshaping.
        var chatMessages = new List<object> { new { role = "system", content = systemPrompt } };
        chatMessages.AddRange(messages.Select(m => (object)new { role = m.Role, content = m.Content }));

        var body = new
        {
            model = modelId,
            messages = chatMessages,
            max_completion_tokens = MaxOutputTokens,
            response_format = schema is { } s
                ? (object)new { type = "json_schema", json_schema = new { name = "structured_reply", strict = false, schema = s } }
                : null
        };

        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/mr444/ImageGenerator.MAUI");
        request.Headers.TryAddWithoutValidation("X-Title", "Emberforge");
        request.Options.Set(RemoteHttpLoggingHandler.PurposeKey, schema is null ? "prompt prose" : "structured prompt");
        request.Options.Set(RemoteHttpLoggingHandler.ModelKey, modelId);

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OpenRouter chat HTTP {StatusCode} Body={Body}",
                (int)response.StatusCode, Truncate(responseBody, 1000));
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} - {ExtractErrorMessage(responseBody) ?? Truncate(responseBody, 200)}",
                inner: null,
                statusCode: response.StatusCode);
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            return ModelReplyText.StripJsonFences(content.GetString() ?? string.Empty);
        }

        throw new InvalidOperationException("OpenRouter response carried no message content.");
    }

    private static string? ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (a proxy's HTML error page, say) — the caller shows a raw snippet.
        }

        return null;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";
}
