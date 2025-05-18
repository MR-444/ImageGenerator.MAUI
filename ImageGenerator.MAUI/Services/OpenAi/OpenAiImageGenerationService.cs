using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Models.OpenAi;

namespace ImageGenerator.MAUI.Services.OpenAi;

public class OpenAiImageGenerationService : IImageGenerationService
{
    public Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
    {
        throw new NotImplementedException();
    }

    //@todo in development.
    private async Task<OpenAiResponse?> CallOpenAiModelAsync(ImageGenerationParameters parameters)
    {
        var finalResponse = new OpenAiResponse
        {
            Data = null,
            Usage = null
        };
        // Return the single URL string
        return finalResponse;
    }
}