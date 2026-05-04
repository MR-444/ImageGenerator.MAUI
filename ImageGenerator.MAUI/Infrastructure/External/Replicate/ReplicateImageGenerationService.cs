using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Models.Replicate;


namespace ImageGenerator.MAUI.Infrastructure.External.Replicate;

public class ReplicateImageGenerationService : IImageGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly IReplicateApi _replicateApi;
    private readonly IModelDescriptorRegistry _registry;

    public ReplicateImageGenerationService(IReplicateApi replicateApi, HttpClient httpClient, IModelDescriptorRegistry registry)
    {
        _replicateApi = replicateApi ?? throw new ArgumentNullException(nameof(replicateApi));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var imageModel = _registry.PayloadFor(parameters.Model).Build(parameters);

            var finalResponse = await CallReplicateModelAsync(parameters, imageModel, cancellationToken);

            if (finalResponse?.Status == ReplicateStatus.Failed)
            {
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

    protected virtual async Task<byte[]> DownloadImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        // The injected HttpClient is the default unnamed factory client, so it doesn't share
        // the Refit pipeline's timeouts. Put a concrete ceiling on the CDN download here.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var response = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cts.Token);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to download image from {imageUrl}", ex);
        }
    }

}
