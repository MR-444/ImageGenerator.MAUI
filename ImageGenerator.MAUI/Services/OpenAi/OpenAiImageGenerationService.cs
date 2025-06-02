using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Models.OpenAi;
using ImageGenerator.MAUI.Models.Factories;

namespace ImageGenerator.MAUI.Services.OpenAi;

public class OpenAiImageGenerationService : IOpenAiImageGenerationService
{
    private readonly IOpenAiApi _openAiApi;

    public OpenAiImageGenerationService(IOpenAiApi openAiApi)
    {
        _openAiApi = openAiApi ?? throw new ArgumentNullException(nameof(openAiApi));
    }

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
    {
        try
        {
            var result = await CallOpenAiModelAsync(parameters);
            if (result?.Data == null || !result.Data.Any())
                throw new InvalidOperationException("OpenAI image generation failed or returned no result.");

            return new GeneratedImage
            {
                Message = "Image generated successfully with OpenAI.",
                FilePath = null,
                ImageDataBase64 = result.Data[0].B64Json
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

    private async Task<OpenAiResponse?> CallOpenAiModelAsync(ImageGenerationParameters parameters)
    {
        // Create the request model using the factory
        var request = (OpenAiRequest)ImageModelFactory.CreateImageModel(parameters);

        // Set additional OpenAI-specific parameters
        request.Size = $"{parameters.Width}x{parameters.Height}";
        request.OutputFormat = parameters.OutputFormat.ToString().ToLower();
        request.OutputCompression = parameters.OutputQuality;
        request.ResponseFormat = "b64_json"; // Required for base64 response

        // Construct the bearer token
        var bearerToken = $"Bearer {parameters.ApiToken}";

        // Make the API call
        return await _openAiApi.CreatePredictionAsync(bearerToken, request);
    }
}