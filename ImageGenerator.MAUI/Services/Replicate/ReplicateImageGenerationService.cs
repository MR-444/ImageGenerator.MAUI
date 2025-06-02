using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Models.Factories;
using ImageGenerator.MAUI.Models.Replicate;
using System.Text;
using ImageGenerator.MAUI.Models.Flux;

namespace ImageGenerator.MAUI.Services.Replicate;

public class ReplicateImageGenerationService(IReplicateApi replicateApi) : IImageGenerationService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "Accept-Encoding", "gzip, deflate" } }
    };

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
    {
        try
        {
            // Use the factory to create the appropriate image model instance
            var imageModel = ImageModelFactory.CreateImageModel(parameters);

            // If we have a base64 image, convert it to data URI format
            if (!string.IsNullOrEmpty(parameters.ImagePrompt))
            {
                if (imageModel is FluxKontextPro kontextPro)
                {
                    kontextPro.InputImage = $"data:image/jpeg;base64,{parameters.ImagePrompt}";
                }
                else if (imageModel is FluxKontextMax kontextMax)
                {
                    kontextMax.InputImage = $"data:image/jpeg;base64,{parameters.ImagePrompt}";
                }
            }

            // Make the call to the generation model
            var finalResponse = await CallReplicateModelAsync(parameters, imageModel);
            if(finalResponse?.Output == null)
                throw new InvalidOperationException("ModelName prediction failed or returned no result.");

            var imageUrl = finalResponse.Output;
            var imageDataBase64 = await DownloadImageAsBase64Async(imageUrl!);

            return new GeneratedImage
            {
                Message = $"Image generated successfully with model {parameters.Model}.",
                FilePath = null,
                ImageDataBase64 = imageDataBase64,
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
    
    private async Task<ReplicatePredictionResponse?> CallReplicateModelAsync(ImageGenerationParameters parameters,
                                                                             ImageModelBase imageModel)
    {
        // Build the request payload
        var replicateRequest = new ReplicatePredictionRequest { Input = imageModel };

        // Construct the bearer token string once
        var bearerToken = $"Bearer {parameters.ApiToken}";

        // Invoke the endpoint using the injected Refit interface
        var response = await replicateApi.CreatePredictionAsync(
            bearerToken,
            parameters.Model,
            replicateRequest
        );
            
        // Poll for final output, using the returned prediction ID
        var finalResponse = await ReplicateHelper.PollForOutputAsync(
            replicateApi,
            bearerToken,
            response.Id ?? string.Empty
        );

        // Return the single URL string
        return finalResponse;
    }
        
    // Download the image from the returned URL
    private static async Task<string> DownloadImageAsBase64Async(string imageUrl)
    {
        try
        {
            var response = await HttpClient.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();
            
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return Convert.ToBase64String(bytes);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to download image from {imageUrl}", ex);
        }
    }
}

public class FileIoResponse
{
    public bool Success { get; set; }
    public string? Link { get; set; }
}