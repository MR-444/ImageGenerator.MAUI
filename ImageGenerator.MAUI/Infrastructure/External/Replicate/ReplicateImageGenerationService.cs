using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using Microsoft.Extensions.Logging;


namespace ImageGenerator.MAUI.Infrastructure.External.Replicate;

public sealed class ReplicateImageGenerationService : IImageGenerationService
{
    internal const string HttpClientName = "replicate-download";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReplicateApi _replicateApi;
    private readonly IModelDescriptorRegistry _registry;
    private readonly ILogger<ReplicateImageGenerationService> _logger;

    public ReplicateImageGenerationService(
        IReplicateApi replicateApi,
        IHttpClientFactory httpClientFactory,
        IModelDescriptorRegistry registry,
        ILogger<ReplicateImageGenerationService> logger)
    {
        _replicateApi = replicateApi ?? throw new ArgumentNullException(nameof(replicateApi));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // A model this build ships no descriptor for is not a model this build can drive: its field set,
    // its vocabularies and its price are all unknown, and the only way to learn them is a billed
    // prediction. So the call is refused here, before anything leaves the machine. (Until now this
    // path sent a prompt and nothing else, which Replicate happily answered at its own defaults.)
    //
    // The check belongs at the one site that builds a Replicate request rather than inside the
    // registry: describing a model and calling it are different permissions, and the narrow
    // capabilities the picker needs for an unknown model stay exactly as they are.
    // progress is unused: the Replicate create→poll flow has no step-level signal worth showing.
    public async Task<GeneratedImage> GenerateImageAsync(
        ImageGenerationParameters parameters,
        CancellationToken cancellationToken = default,
        IProgress<JobProgress>? progress = null)
    {
        try
        {
            if (!_registry.IsSchemaKnown(parameters.Model))
                return Refused(parameters.Model, "this build has no field list for it, so it is not supported");

            var caps = _registry.CapabilitiesFor(parameters.Model).Capabilities;
            // A descriptor is a hand-written claim about a model that keeps changing underneath it,
            // and this is the only thing standing between a stale claim and money spent on a 422.
            if (ValueOutsideTheModelVocabulary(parameters, caps) is { } offVocabulary)
                return Refused(parameters.Model, $"it does not accept {offVocabulary}");

            var imageModel = _registry.PayloadFor(parameters.Model).Build(parameters);

            var finalResponse = await CallReplicateModelAsync(parameters, imageModel, cancellationToken);

            if (finalResponse?.Status == ReplicateStatus.Failed)
            {
                _logger.LogError(
                    "Replicate prediction failed Model={Model} Error={Error}",
                    parameters.Model,
                    finalResponse.Error ?? "no error message");
                return new GeneratedImage
                {
                    Message = $"Image generation failed: {finalResponse.Error ?? "no error message"}.",
                    ImageData = null
                };
            }

            if (finalResponse?.Status == ReplicateStatus.Canceled)
            {
                return new GeneratedImage
                {
                    Message = "Image generation was canceled.",
                    ImageData = null
                };
            }

            var outputUrl = finalResponse?.Output?.FirstOrDefault();
            if (string.IsNullOrEmpty(outputUrl))
            {
                var errorMessage = finalResponse?.Error ?? "Unknown error";
                var status = finalResponse?.Status ?? "Unknown status";
                throw new InvalidOperationException($"Model prediction returned no output. Status: {status}, Error: {errorMessage}");
            }

            var imageData = await DownloadImageAsync(outputUrl, cancellationToken);

            return new GeneratedImage
            {
                Message = $"Image generated successfully with model {parameters.Model}.",
                ImageData = imageData
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
            _logger.LogError(ex, "ReplicateImageGenerationService.GenerateImageAsync threw Model={Model}", parameters.Model);
            return new GeneratedImage
            {
                Message = FormatError(ex, parameters.ApiToken),
                ImageData = null
            };
        }
    }

    /// <summary>
    /// A call that was stopped before it reached the network, so nothing was billed. Returned rather
    /// than thrown so the reason lands on the job card where the failed generation would have been.
    /// </summary>
    private GeneratedImage Refused(string modelId, string because)
    {
        var message = $"{modelId}: {because}. No request was sent, so nothing was billed.";
        _logger.LogWarning("Refused Replicate call: {Message}", message);
        return new GeneratedImage { Message = message, ImageData = null };
    }

    /// <summary>
    /// The name of the first request value the resolved descriptor does not declare, or null. The
    /// picker cannot produce an off-vocabulary value — <c>RefreshCapabilities</c> snaps every knob to
    /// the selected model on change — but restored settings, a cloned run, or a descriptor that has
    /// drifted from its model all can, and each of those currently costs a prediction to discover.
    ///
    /// A field with no declared vocabulary is skipped rather than refused: it is a knob this build
    /// does not offer, and the descriptor does not send it either.
    /// </summary>
    private static string? ValueOutsideTheModelVocabulary(ImageGenerationParameters p, ModelCapabilities caps) =>
        caps.AspectRatio && caps.AspectRatios.Count > 0 && !caps.AspectRatios.Contains(p.AspectRatio, StringComparer.Ordinal)
            ? $"aspect ratio {p.AspectRatio}" // "custom" is legal here: descriptors that take it list it.
            : Outside(p.GptQuality, caps.GptQualityOptions, "quality")
            ?? Outside(p.GptModeration, caps.GptModerationOptions, "moderation")
            ?? Outside(p.GptComplexity, caps.GptComplexityOptions, "complexity")
            ?? Outside(p.GptBackground, caps.GptBackgroundOptions, "background")
            ?? Outside(p.GptInputFidelity, caps.GptInputFidelityOptions, "input fidelity");

    private static string? Outside(string? value, IReadOnlyList<string>? vocabulary, string field) =>
        vocabulary is { Count: > 0 } && value is not null && !vocabulary.Contains(value, StringComparer.Ordinal)
            ? $"{field} {value}"
            : null;

    private string FormatError(Exception ex, string apiToken)
    {
        string result;
        // Refit wraps non-2xx as ApiException with StatusCode + body. Deserialization failures
        // (gzip not decompressed, malformed JSON) also come through as ApiException — with the
        // real cause buried on InnerException while .Content reads empty.
        if (ex is Refit.ApiException api)
        {
            var body = string.IsNullOrWhiteSpace(api.Content) ? "(no body)" : api.Content;
            var head = $"HTTP {(int)api.StatusCode} {api.StatusCode}: {body}";
            result = api.InnerException != null ? $"{head} — inner: {api.InnerException.Message}" : head;

            // A rejected payload is the one failure whose fix is written inside the response, and
            // the status label is too small to read it in JSON form. 400 and 422 are the two codes
            // Replicate uses for input validation; anything else keeps the raw dump verbatim.
            if ((int)api.StatusCode is 400 or 422
                && ReplicateValidationErrors.TrySummarize(api.Content, out var summary))
            {
                // The raw body still goes to the log: the summary is truncated, and a summary that
                // quietly replaced a body we mis-parsed would be worse than the unreadable one it
                // replaced.
                _logger.LogWarning(
                    "Replicate rejected the input; raw body: {Body}", string.IsNullOrWhiteSpace(api.Content) ? "(no body)" : api.Content);
                result = $"HTTP {(int)api.StatusCode}: {summary}";
            }
        }
        else
        {
            // HttpRequestException.Message is usually "An error occurred while sending a request.";
            // the actionable detail lives on the inner (SocketException, AuthenticationException, ...).
            var deepest = ex;
            while (deepest.InnerException != null) deepest = deepest.InnerException;
            result = deepest.Message == ex.Message
                ? $"An error occurred: {ex.Message}"
                : $"An error occurred: {ex.Message} ({deepest.Message})";
        }

        // Defense-in-depth: bearer token lives on the Authorization header today, not in the
        // request body or URL, so it can't reach the surfaced error string. Redact anyway so
        // a future code change that puts the token on a logged path can't leak it to the UI.
        if (!string.IsNullOrEmpty(apiToken))
        {
            result = result.Replace(apiToken, "[REDACTED]");
        }
        return result;
    }

    private async Task<ReplicatePredictionResponse?> CallReplicateModelAsync(
        ImageGenerationParameters parameters,
        object imageModel,
        CancellationToken cancellationToken)
    {
        var replicateRequest = new ReplicatePredictionRequest { Input = imageModel };
        var bearerToken = $"Bearer {parameters.ApiToken}";

        var response = await _replicateApi.CreatePredictionAsync(
            bearerToken,
            parameters.Model,
            replicateRequest,
            cancellationToken
        );

        return await ReplicateHelper.PollForOutputAsync(
            _replicateApi,
            bearerToken,
            response.Id ?? string.Empty,
            cancellationToken
        );
    }

    private async Task<byte[]> DownloadImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        // The named client carries the standard resilience pipeline (Polly retry + per-attempt /
        // total timeouts). The factory hands out a lightweight wrapper around a pooled handler;
        // dispose it per call.
        using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to download image from {imageUrl}", ex);
        }
    }

}
