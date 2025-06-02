using ImageGenerator.MAUI.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageGenerator.MAUI.Services;

public class ImageFileService : IImageFileService
{
    private readonly IImageEncoderProvider _encoderProvider;

    public ImageFileService(IImageEncoderProvider encoderProvider)
    {
        _encoderProvider = encoderProvider;
    }

    public async Task SaveImageWithMetadataAsync(string imagePath, byte[] imageBytes, ImageGenerationParameters parameters)
    {
        using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(new MemoryStream(imageBytes));

        var metadataText = $"""
                            Prompt: {parameters.Prompt}
                            ModelName: {parameters.Model}
                            Seed: {parameters.Seed}
                            AspectRatio: {parameters.AspectRatio}
                            Dimensions: {parameters.Width}x{parameters.Height}
                            Format: {parameters.OutputFormat}
                            Quality: {parameters.OutputQuality}
                            Upsampling: {parameters.PromptUpsampling}
                            """;

        image.Metadata.ExifProfile ??= new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.UserComment, metadataText);

        var encoder = _encoderProvider.GetImageEncoder(parameters.OutputFormat, parameters.OutputQuality);
        await image.SaveAsync(imagePath, encoder);
    }

    public string BuildFileName(ImageGenerationParameters parameters)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var invalidChars = Path.GetInvalidFileNameChars();
        var safePrompt = new string(parameters.Prompt.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray())
            .Replace(" ", "_")
            .Replace("__", "_");
        safePrompt = safePrompt.Length > 30 ? safePrompt[..30] : safePrompt;
        var fileExtension = parameters.OutputFormat.ToString().ToLowerInvariant();
        return $"{timestamp}_{safePrompt}_{parameters.Seed}.{fileExtension}";
    }
}
