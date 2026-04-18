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
                var dataUri = parameters.ImagePrompt.StartsWith("data:")
                    ? parameters.ImagePrompt
                    : $"data:{DetectImageMimeType(parameters.ImagePrompt)};base64,{parameters.ImagePrompt}";

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
            if (finalResponse?.Output == null)
            {
                var errorMessage = finalResponse?.Error ?? "Unknown error";
                var status = finalResponse?.Status ?? "Unknown status";
                throw new InvalidOperationException($"Model prediction failed or returned no result. Status: {status}, Error: {errorMessage}");
            }

            var imageDataBase64 = await DownloadImageAsBase64Async(finalResponse.Output!, cancellationToken);

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
                Message = $"An error occurred: {ex.Message}",
                FilePath = null,
                ImageDataBase64 = null
            };
        }
    }

    private async Task<ReplicatePredictionResponse?> CallReplicateModelAsync(
        ImageGenerationParameters parameters,
        ImageModelBase imageModel,
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

        // If the Prefer: wait returned a terminal state already, skip polling.
        if (response.Status is "succeeded" or "failed" or "canceled")
        {
            return response;
        }

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

    private static string DetectImageMimeType(string base64Data)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Data[..Math.Min(base64Data.Length, 20)]);
        }
        catch (FormatException)
        {
            return "image/png";
        }

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return "image/gif";

        if (bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";

        return "image/png";
    }
}
