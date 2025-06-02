using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Services.OpenAi;
using ImageGenerator.MAUI.Services.Replicate;

namespace ImageGenerator.MAUI.Services;

public class ImageGenerationServiceFactory : IImageGenerationServiceFactory
{
    private readonly IImageGenerationService _openAiService;
    private readonly IImageGenerationService _replicateService;

    public ImageGenerationServiceFactory(
        IOpenAiImageGenerationService openAiService,
        IReplicateImageGenerationService replicateService)
    {
        _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));
        _replicateService = replicateService ?? throw new ArgumentNullException(nameof(replicateService));
    }

    public IImageGenerationService GetService(string modelName)
    {
        return modelName switch
        {
            ModelConstants.OpenAI.GptImage1 => _openAiService,
            _ => _replicateService
        };
    }

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
    {
        // Route to the appropriate service based on the model
        return parameters.Model switch
        {
            ModelConstants.OpenAI.GptImage1 => await _openAiService.GenerateImageAsync(parameters),
            _ => await _replicateService.GenerateImageAsync(parameters)
        };
    }
} 