using ImageGenerator.MAUI.Models;

namespace ImageGenerator.MAUI.Services;

/// <summary>
/// Defines the contract for a service responsible for generating images based on
/// the provided parameters. Implementors of this interface should encapsulate
/// specific image generation logic and technologies.
/// </summary>
public interface IImageGenerationService
{
    /// <summary>
    /// Asynchronously generates an image based on the provided parameters.
    /// </summary>
    /// <param name="parameters">
    /// The parameters that define the image generation process, including details such as model selection,
    /// image attributes, and any other customizable options.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result is a <see cref="GeneratedImage"/> object
    /// containing the generated image details.
    /// </returns>
    Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters);
}
