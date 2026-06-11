using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
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

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken = default)
    {
        try
        {
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

    private static string FormatError(Exception ex, string apiToken)
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
