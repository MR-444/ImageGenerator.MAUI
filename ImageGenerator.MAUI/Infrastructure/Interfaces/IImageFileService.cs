using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Provides functionalities for saving image files with accompanying metadata, and
/// generating file names based on specified image generation parameters.
/// </summary>
public interface IImageFileService
{
    /// <summary>
    /// Asynchronously saves an image to the specified file path and associates it with metadata
    /// based on the provided image generation parameters.
    /// </summary>
    /// <param name="imagePath">The file path where the image should be saved.</param>
    /// <param name="imageBytes">The raw byte data of the image to save.</param>
    /// <param name="parameters">The parameters containing metadata associated with the image generation process.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SaveImageWithMetadataAsync(string imagePath, byte[] imageBytes, ImageGenerationParameters parameters);

    /// <summary>
    /// Generates a file name based on the provided image generation parameters.
    /// </summary>
    /// <param name="parameters">The parameters used for generating the image, which influence the generated file name.</param>
    /// <returns>A string representing the generated file name.</returns>
    string BuildFileName(ImageGenerationParameters parameters);
}
