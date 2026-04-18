using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Core.Application.Services;

public class ImageGenerationOrchestrator(IImageGenerationServiceFactory serviceFactory) : IImageGenerationService
{
    private readonly IImageGenerationServiceFactory _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));

    public Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken = default)
    {
        var service = _serviceFactory.GetService(parameters.Model);
        return service.GenerateImageAsync(parameters, cancellationToken);
    }
}
