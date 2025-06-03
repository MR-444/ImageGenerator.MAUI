using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi.Interfaces;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Application.Services;

/// <summary>
/// Factory class responsible for providing the correct image generation service
/// based on the specified model name.
/// </summary>
public class ImageGenerationServiceFactory : IImageGenerationServiceFactory
{
    /// <summary>
    /// Represents the OpenAI image generation service used within the factory to handle
    /// requests for generating images using the OpenAI model.
    /// </summary>
    private readonly IImageGenerationService _openAiService;

    /// <summary>
    /// Represents an instance of the image generation service that utilizes the Replicate platform for creating images.
    /// This service is selected when the requested model does not match the known OpenAI model constants.
    /// </summary>
    private readonly IImageGenerationService _replicateService;

    /// <summary>
    /// Factory for creating instances of <see cref="IImageGenerationService"/> based on the provided model name.
    /// </summary>
    public ImageGenerationServiceFactory(
        IOpenAiImageGenerationService openAiService,
        IReplicateImageGenerationService replicateService)
    {
        _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));
        _replicateService = replicateService ?? throw new ArgumentNullException(nameof(replicateService));
    }

    /// <summary>
    /// Retrieves an image generation service based on the specified model name.
    /// </summary>
    /// <param name="modelName">The name of the model for which the corresponding image generation service is required.</param>
    /// <returns>An implementation of <see cref="IImageGenerationService"/> corresponding to the provided model name.</returns>
    public IImageGenerationService GetService(string modelName)
    {
        return modelName switch
        {
            ModelConstants.OpenAI.GptImage1 => _openAiService,
            _ => _replicateService
        };
    }
} 