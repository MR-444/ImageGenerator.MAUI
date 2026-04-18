using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Factories;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi.Interfaces;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Infrastructure.External.OpenAi;

public class OpenAiImageGenerationService : IOpenAiImageGenerationService
{
    private readonly IOpenAiApi _openAiApi;

    public OpenAiImageGenerationService(IOpenAiApi openAiApi)
    {
        _openAiApi = openAiApi ?? throw new ArgumentNullException(nameof(openAiApi));
    }

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await CallOpenAiModelAsync(parameters, cancellationToken);
            if (result?.Data == null || result.Data.Count == 0)
                throw new InvalidOperationException("OpenAI image generation failed or returned no result.");

            return new GeneratedImage
            {
                Message = "Image generated successfully with OpenAI.",
                FilePath = null,
                ImageDataBase64 = result.Data[0].B64Json
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

    private async Task<OpenAiResponse?> CallOpenAiModelAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken)
    {
        var request = (OpenAiRequest)ImageModelFactory.CreateImageModel(parameters);

        request.Size = $"{parameters.Width}x{parameters.Height}";
        request.OutputFormat = parameters.OutputFormat.ToString().ToLower();
        request.OutputCompression = parameters.OutputQuality;
        request.ResponseFormat = "b64_json";

        var bearerToken = $"Bearer {parameters.ApiToken}";

        return await _openAiApi.CreatePredictionAsync(bearerToken, request, cancellationToken);
    }
}
