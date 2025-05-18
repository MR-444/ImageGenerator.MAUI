using ImageGenerator.MAUI.Models;

namespace ImageGenerator.MAUI.Services;

public interface IImageGenerationService
{
    Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters);
}
