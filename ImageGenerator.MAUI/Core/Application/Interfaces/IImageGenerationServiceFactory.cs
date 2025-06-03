namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Defines a factory for creating instances of image generation services
/// based on a specified model name.
/// </summary>
public interface IImageGenerationServiceFactory
{
    /// <summary>
    /// Retrieves an image generation service based on the specified model name.
    /// </summary>
    /// <param name="modelName">The name of the model for which the corresponding image generation service is required.</param>
    /// <returns>An instance of IImageGenerationService corresponding to the provided model name.</returns>
    IImageGenerationService GetService(string modelName);
} 