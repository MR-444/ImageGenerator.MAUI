using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Factories;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Models.Replicate;


namespace ImageGenerator.MAUI.Infrastructure.External.Replicate;

public class ReplicateImageGenerationService : IReplicateImageGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly IReplicateApi _replicateApi;

    public ReplicateImageGenerationService(IReplicateApi replicateApi, HttpClient httpClient)
    {
        _replicateApi = replicateApi ?? throw new ArgumentNullException(nameof(replicateApi));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var imageModel = ImageModelFactory.CreateImageModel(parameters);

            if (!string.IsNullOrEmpty(parameters.ImagePrompt))
            {
                var dataUri = ReplicateImageEncoding.BuildDataUri(parameters.ImagePrompt);
                switch (imageModel)
                {
                    case FluxKontextPro kontextPro:
                        kontextPro.InputImage = dataUri;
                        break;
                    case FluxKontextMax kontextMax:
                        kontextMax.InputImage = dataUri;
                        break;
                }
            }

            var finalResponse = await CallReplicateModelAsync(parameters, imageModel, cancellationToken);
            var outputUrl = finalResponse?.Output?.FirstOrDefault();
            if (string.IsNullOrEmpty(outputUrl))
            {
                var errorMessage = finalResponse?.Error ?? "Unknown error";
                var status = finalResponse?.Status ?? "Unknown status";
                throw new InvalidOperationException($"Model prediction failed or returned no result. Status: {status}, Error: {errorMessage}");
            }

            var imageDataBase64 = await DownloadImageAsBase64Async(outputUrl, cancellationToken);

            return new GeneratedImage
            {
                Message = $"Image generated successfully with model {parameters.Model}.",
                FilePath = null,
                ImageDataBase64 = imageDataBase64
            };
        }
        catch (OperationCanceledException)
        {
            return new GeneratedImage
            {
                Message = "Image generation was canceled.",
                FilePath = null,
                ImageDataBase64 = null
            };
        }
        catch (Exception ex)
        {
            return new GeneratedImage
            {
                Message = FormatError(ex),
                FilePath = null,
                ImageDataBase64 = null
            };
        }
    }

    private static string FormatError(Exception ex)
    {
        // Refit wraps non-2xx as ApiException with StatusCode + body. Deserialization failures
        // (gzip not decompressed, malformed JSON) also come through as ApiException — with the
        // real cause buried on InnerException while .Content reads empty.
        if (ex is Refit.ApiException api)
        {
            var body = string.IsNullOrWhiteSpace(api.Content) ? "(no body)" : api.Content;
            var head = $"HTTP {(int)api.StatusCode} {api.StatusCode}: {body}";
            return api.InnerException != null ? $"{head} — inner: {api.InnerException.Message}" : head;
        }
        // HttpRequestException.Message is usually "An error occurred while sending a request.";
        // the actionable detail lives on the inner (SocketException, AuthenticationException, ...).
        var deepest = ex;
        while (deepest.InnerException != null) deepest = deepest.InnerException;
        return deepest.Message == ex.Message
            ? $"An error occurred: {ex.Message}"
            : $"An error occurred: {ex.Message} ({deepest.Message})";
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

    protected virtual async Task<string> DownloadImageAsBase64Async(string imageUrl, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(imageUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return Convert.ToBase64String(bytes);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to download image from {imageUrl}", ex);
        }
    }

}
