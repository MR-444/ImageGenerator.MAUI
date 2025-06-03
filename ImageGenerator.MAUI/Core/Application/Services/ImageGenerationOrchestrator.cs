using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Core.Application.Services;

/// <summary>
/// The ImageGenerationOrchestrator class coordinates the generation of images by delegating requests
/// to the appropriate image generation service based on the specified model type.
/// </summary>
/// <remarks>
/// This class implements the IImageGenerationService interface by utilizing an IImageGenerationServiceFactory
/// to identify and execute the appropriate image generation logic for the given parameters. It provides
/// an abstraction that simplifies the process of generating images across different service implementations.
/// </remarks>
/// <example>
/// This class depends on IImageGenerationServiceFactory to provide the correct implementation of
/// IImageGenerationService based on the input model. The GenerateImageAsync method performs the execution
/// of the image generation workflow asynchronously.
/// </example>
public class ImageGenerationOrchestrator(IImageGenerationServiceFactory serviceFactory) : IImageGenerationService
{
    /// <summary>
    /// Provides a factory instance for creating specific implementations of
    /// <see cref="IImageGenerationService"/> based on the model name.
    /// This factory is used to delegate image generation tasks to the appropriate service.
    /// </summary>
    private readonly IImageGenerationServiceFactory _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));

    /// <summary>
    /// Generates an image based on the provided parameters using the appropriate image generation service.
    /// </summary>
    /// <param name="parameters">The parameters containing the details required to generate the image, including the model name.</param>
    /// <returns>A task representing the asynchronous operation that returns a <see cref="GeneratedImage"/> containing the generated image and its associated metadata.</returns>
    public async Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters)
    {
        var service = _serviceFactory.GetService(parameters.Model);
        return await service.GenerateImageAsync(parameters);
    }
} 