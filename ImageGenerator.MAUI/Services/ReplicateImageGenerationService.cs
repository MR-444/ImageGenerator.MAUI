using ImageGenerator.MAUI.Models;

namespace ImageGenerator.MAUI.Services;

public class ReplicateImageGenerationService(IReplicateApi replicateApi) : IImageGenerationService
{
    private static readonly HttpClient HttpClient = new();

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
    {
        try
        {
            // Make the call to the generation model
            var finalResponse = await CallReplicateModelAsync(parameters);
            if(finalResponse?.Output == null)
                throw new InvalidOperationException("Model prediction failed or returned no result.");

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
    
    private async Task<ReplicatePredictionResponse?> CallReplicateModelAsync(ImageGenerationParameters parameters)
    {
        // Build the request payload
        var replicateRequest = new ReplicatePredictionRequest
        {
            Input = new ReplicateInput
            {
                Prompt = parameters.Prompt,
                PromptUpsampling = parameters.PromptUpsampling,
                Seed = parameters.Seed,
                Width = parameters.Width,
                Height = parameters.Height,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat,
                OutputQuality = parameters.OutputQuality
            }
        };

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
        var response = await HttpClient.GetAsync(imageUrl);
        response.EnsureSuccessStatusCode();
        
        var bytes = await response.Content.ReadAsByteArrayAsync();

        // return just as base64, ABI safe and simple
        return Convert.ToBase64String(bytes);
    }
}