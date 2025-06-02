using ImageGenerator.MAUI.Models;

namespace ImageGenerator.MAUI.Services;

public interface IImageFileService
{
    Task SaveImageWithMetadataAsync(string imagePath, byte[] imageBytes, ImageGenerationParameters parameters);
    string BuildFileName(ImageGenerationParameters parameters);
}
