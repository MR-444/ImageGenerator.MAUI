using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

public interface IImageFileService
{
    /// <summary>Saves the image to <paramref name="imagePath"/> with parameter metadata embedded.</summary>
    Task SaveImageWithMetadataAsync(string imagePath, byte[] imageBytes, ImageGenerationParameters parameters);

    /// <summary>Builds a filename (no directory) from the parameters. Caller combines with a directory.</summary>
    string BuildFileName(ImageGenerationParameters parameters);

    /// <summary>
    /// Returns a full path inside <paramref name="directory"/> that does not collide with an existing file.
    /// Appends _1, _2, ... before the extension if the base filename is already taken.
    /// </summary>
    string GetUniqueSavePath(string directory, ImageGenerationParameters parameters);
}
