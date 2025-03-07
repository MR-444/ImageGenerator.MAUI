using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.Models;

namespace ImageGenerator.MAUI.Services;

public class ReplicateImageGenerationService(IReplicateApi replicateApi) : IImageGenerationService
{
    private static readonly HttpClient HttpClient = new();

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
    {
        try
        {
            // Validate all input parameters
            var validationError = ValidateParameters(parameters);
            if (validationError != null)
            {
                return new GeneratedImage
                {
                    Message = $"Validation Error: {validationError}",
                    FilePath = null,
                    ImageDataBase64 = null
                };
            }

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

    // Validation logic
    private static string? ValidateParameters(ImageGenerationParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.ApiToken))
            return "ApiToken cannot be empty.";
                
        // Prompt (required, cannot be empty)
        if (string.IsNullOrWhiteSpace(p.Prompt))
            return "Prompt cannot be empty.";
            
        // Aspect Ratio (required, cannot be empty, optionally check for valid values)
        if (string.IsNullOrWhiteSpace(p.AspectRatio))
            return "Aspect Ratio cannot be empty.";
            
        // Image Prompt (required, cannot be empty)
        if (string.IsNullOrWhiteSpace(p.ImagePrompt) && string.IsNullOrWhiteSpace(p.Prompt) )
            return "Image Prompt cannot be empty, if no prompt is provided.";

        // Seed
        if (p.Seed < 0 || p.Seed > ValidationConstants.SeedMaxValue)
            return $"Seed must be between 0 and {ValidationConstants.SeedMaxValue}.";

        // Safety Tolerance
        if (p.SafetyTolerance is < ValidationConstants.SliderSafetyMin or > ValidationConstants.SliderSafetyMax)
            return $"Safety Tolerance must be between {ValidationConstants.SliderSafetyMin} and {ValidationConstants.SliderSafetyMax}.";

        // Width
        if (p.Width is < ValidationConstants.ImageWidthMin or > ValidationConstants.ImageWidthMax)
            return $"Width must be between {ValidationConstants.ImageWidthMin} and {ValidationConstants.ImageWidthMax}.";
        if (p.Width % 16 != 0)
            return "Width must be a multiple of 16.";

        // Height
        if (p.Height < ValidationConstants.ImageHeightMin || p.Height > ValidationConstants.ImageHeightMax)
            return $"Height must be between {ValidationConstants.ImageHeightMin} and {ValidationConstants.ImageHeightMax}.";
        if (p.Height % 16 != 0)
            return "Height must be a multiple of 16.";

        // Output Quality
        if (p.OutputQuality is < ValidationConstants.SliderOutputQualityMin or > ValidationConstants.SliderOutputQualityMax)
            return $"Output Quality must be between {ValidationConstants.SliderOutputQualityMin} and {ValidationConstants.SliderOutputQualityMax}.";

        // All validations passed
        return null; 
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