using ImageGenerator.MAUI.Models;

namespace ImageGenerator.MAUI.Services;

public class ImageGenerationOrchestrator : IImageGenerationService
{
    private readonly IImageGenerationServiceFactory _serviceFactory;

    public ImageGenerationOrchestrator(IImageGenerationServiceFactory serviceFactory)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
    }

    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
    {
        var service = _serviceFactory.GetService(parameters.Model);
        return await service.GenerateImageAsync(parameters);
    }
} 