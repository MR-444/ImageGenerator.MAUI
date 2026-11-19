using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Pollinations;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Pollinations;

public sealed class PollinationsImageGenerationService : IImageGenerationService
{
    internal const string HttpClientName = "pollinations";

    // gen.pollinations.ai is the active host (matches the Python reference). The legacy
    // image.pollinations.ai /models endpoint now only returns ["sana"], so it's effectively
    // dead for our purposes.
    private const string PromptBaseUrl = "https://gen.pollinations.ai/image/";

    // Pollinations returns an error page (HTML) on bad requests / quota with 200 OK in some
    // cases. The Python reference uses 1000 bytes as a sanity floor to reject those — port the
    // same guard, since a real generated JPEG is always 5-200+ KB.
    private const int MinValidImageBytes = 1000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IModelDescriptorRegistry _registry;
    private readonly ILogger<PollinationsImageGenerationService> _logger;

    public PollinationsImageGenerationService(
        IHttpClientFactory httpClientFactory,
        IModelDescriptorRegistry registry,
        ILogger<PollinationsImageGenerationService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // progress is unused: the Pollinations GET returns in one shot, there is nothing to report.
    public async Task<GeneratedImage> GenerateImageAsync(
        ImageGenerationParameters parameters,
        CancellationToken cancellationToken = default,
        IProgress<JobProgress>? progress = null)
    {
        try
        {
            if (_registry.PayloadFor(parameters.Model).Build(parameters) is not PollinationsRequest request)
            {
                var msg = $"Pollinations descriptor for '{parameters.Model}' did not return a PollinationsRequest payload.";
                _logger.LogError("Pollinations descriptor mismatch Model={Model}", parameters.Model);
                return new GeneratedImage
                {
                    Message = msg,
                    ImageData = null
                };
            }

            // Pollinations documents seed as `min: -1, max: 2147483647` (positive int32) and
            // rejects anything outside that with HTTP 400. The app's global SeedMaxValue is
            // uint32 max because Replicate Flux accepts the wider range, so we clamp at the
            // wire boundary rather than constraining the entity. Idempotent for seeds already
            // in range; -1 (Pollinations' "random" sentinel) is left alone.
            if (request.Seed > int.MaxValue)
            {
                request = request with { Seed = request.Seed & int.MaxValue };
            }

            var url = BuildUrl(request);

            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(parameters.PollinationsApiToken))
            {
                httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {parameters.PollinationsApiToken}");
            }

            using var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // EnsureSuccessStatusCode throws an HttpRequestException whose Message is the
                // status line only — the actual reason is in the body. Read it manually so the
                // user sees "Pollinations HTTP 400 BadRequest: invalid model 'foo'" instead of
                // a generic "Bad Request".
                string fullBody;
                try
                {
                    fullBody = await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception readEx)
                {
                    fullBody = $"(body read failed: {readEx.Message})";
                }
                if (string.IsNullOrWhiteSpace(fullBody)) fullBody = "(no body)";

                // app.log gets the full body + URL so the user can see whatever Pollinations
                // actually complained about; the UI status line is truncated to keep the
                // job-row label readable. URL is included so the failed request is reproducible.
                _logger.LogError(
                    "Pollinations HTTP {StatusCode} {Status} Url={Url} Body={Body}",
                    (int)response.StatusCode,
                    response.StatusCode,
                    Redact(url, parameters.PollinationsApiToken),
                    Redact(fullBody, parameters.PollinationsApiToken));

                return new GeneratedImage
                {
                    // The full body, not a truncation of it: the remedy for a refusal is carried inside
                    // the JSON error envelope, and cutting the body at 500 characters can cut that
                    // envelope apart. Truncation happens where the text is turned into a job-row label.
                    Message = Redact(
                        DescribeFailure(response.StatusCode, fullBody),
                        parameters.PollinationsApiToken),
                    ImageData = null
                };
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length >= MinValidImageBytes)
                return new GeneratedImage
                {
                    Message = $"Image generated successfully with model {parameters.Model}.",
                    ImageData = bytes
                };
            _logger.LogError(
                "Pollinations undersized response Bytes={Bytes} Url={Url}",
                bytes.Length,
                Redact(url, parameters.PollinationsApiToken));
            return new GeneratedImage
            {
                Message = $"Pollinations returned an undersized response ({bytes.Length} bytes) — likely an error page rather than an image.",
                ImageData = null
            };

        }
        catch (OperationCanceledException)
        {
            return new GeneratedImage
            {
                Message = "Image generation was canceled.",
                ImageData = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PollinationsImageGenerationService.GenerateImageAsync threw Model={Model}", parameters.Model);
            return new GeneratedImage
            {
                Message = FormatError(ex, parameters.PollinationsApiToken),
                ImageData = null
            };
        }
    }

    private static string BuildUrl(PollinationsRequest r)
    {
        var encodedPrompt = Uri.EscapeDataString(r.Prompt);

        var query = new StringBuilder();
        Append(query, "model", r.Model);
        Append(query, "width", r.Width.ToString(CultureInfo.InvariantCulture));
        Append(query, "height", r.Height.ToString(CultureInfo.InvariantCulture));
        Append(query, "seed", r.Seed.ToString(CultureInfo.InvariantCulture));
        Append(query, "enhance", "false");
        // gptimage family only — the descriptor leaves this null for every other model, and an
        // unsupported model would 400 on it rather than ignore it.
        if (!string.IsNullOrWhiteSpace(r.Quality)) Append(query, "quality", r.Quality);
        // Spec: `safe` takes a comma-separated category list (privacy, secrets, sexual,
        // violence, shield, true, nsfw). Boolean true enables privacy+secrets; "nsfw" enables
        // sexual+violence (what the UI checkbox labels "NSFW filter"). Default is off — omit
        // the param when the user hasn't enabled it.
        if (r.Safe) Append(query, "safe", "nsfw");

        return $"{PromptBaseUrl}{encodedPrompt}?{query}";
    }

    // Pollinations answers every failure with the same JSON envelope:
    //   { "status": 402, "success": false, "error": { "code": "KEY_BUDGET_EXHAUSTED", "message": "…" } }
    // The code is the only field that says what actually happened. 402 is documented as covering three
    // causes — INSUFFICIENT_BALANCE, KEY_BUDGET_EXHAUSTED, QUEST_POLLEN_ONLY — and two of them describe
    // the account or the key rather than the model, so a message naming the model without reading the
    // code is wrong about as often as it is right. An unrecognised code, a body that is not the
    // envelope, and every other status all fall through to quoting upstream, one rule for all of them.
    private static string DescribeFailure(HttpStatusCode statusCode, string fullBody)
    {
        var (code, message) = ReadErrorEnvelope(fullBody);
        var trimmed = fullBody.Length > 500 ? fullBody[..500] + "…" : fullBody;

        // 401 means "missing or invalid API key" whatever the code says, so the status alone is enough
        // to say something useful; the upstream sentence still rides along because it is what
        // distinguishes the two, and a pk_ app key is refused since 2026-11-01.
        if (statusCode == HttpStatusCode.Unauthorized)
            return QuotingUpstream(
                "Pollinations refused an unauthenticated request — add a secret (sk_) API key in Settings (get one at enter.pollinations.ai/keys). A publishable app key (pk_) only identifies an app and is refused on its own.",
                message, trimmed);

        if (statusCode == HttpStatusCode.PaymentRequired)
        {
            var sentence = code switch
            {
                "INSUFFICIENT_BALANCE" =>
                    "Pollinations: the account behind this key is out of pollen. Top up at enter.pollinations.ai — this is the account's balance, not a price tag on the model.",
                "KEY_BUDGET_EXHAUSTED" =>
                    "Pollinations: this API key has spent the budget set on it. Raise that budget at enter.pollinations.ai/keys or use another key — the model was never the problem.",
                "QUEST_POLLEN_ONLY" =>
                    "Pollinations: this request needs pollen this key may not spend — the account may hold only Quest Pollen. That is a limit on the account, not proof that the model is marked paid.",
                _ => null,
            };
            if (sentence is not null) return QuotingUpstream(sentence, message, trimmed);
        }

        return $"Pollinations HTTP {(int)statusCode} {statusCode}: {trimmed}";
    }

    // The upstream sentence is the only thing naming the balance, budget or model it complained about, so
    // it survives the translation instead of being replaced by it.
    private static string QuotingUpstream(string sentence, string? message, string trimmedBody) =>
        $"{sentence} Pollinations said: {(string.IsNullOrWhiteSpace(message) ? trimmedBody : message)}";

    private static (string? Code, string? Message) ReadErrorEnvelope(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
                return (null, null);

            return (ReadString(error, "code"), ReadString(error, "message"));
        }
        catch (JsonException)
        {
            // A gateway in front of the API can answer in HTML, and an error page is not an envelope.
            // No code to read means no claim to make, which the caller handles by quoting the body.
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void Append(StringBuilder sb, string key, string value)
    {
        if (sb.Length > 0) sb.Append('&');
        sb.Append(key).Append('=').Append(Uri.EscapeDataString(value));
    }

    private static string FormatError(Exception ex, string apiToken)
    {
        // Walk to the deepest inner exception — HttpRequestException.Message is usually generic
        // ("An error occurred while sending a request."); the actionable detail lives inside.
        var deepest = ex;
        while (deepest.InnerException != null) deepest = deepest.InnerException;
        var result = deepest.Message == ex.Message
            ? $"An error occurred: {ex.Message}"
            : $"An error occurred: {ex.Message} ({deepest.Message})";

        return Redact(result, apiToken);
    }

    // Defense-in-depth: token lives on Authorization header today, not in URL/body, so it
    // shouldn't appear in error text — redact anyway so a future code change can't leak it.
    private static string Redact(string text, string? apiToken) =>
        string.IsNullOrEmpty(apiToken) ? text : text.Replace(apiToken, "[REDACTED]");
}
