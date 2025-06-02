using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageGenerator.MAUI.Services;

public class ImageFileService : IImageFileService
{
    public async Task SaveImageWithMetadataAsync(string imagePath, byte[] imageBytes, ImageGenerationParameters parameters)
    {
        using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(new MemoryStream(imageBytes));

        var metadataText =
            $"Prompt: {parameters.Prompt}\n" +
            $"ModelName: {parameters.Model}\n" +
            $"Seed: {parameters.Seed}\n" +
            $"AspectRatio: {parameters.AspectRatio}\n" +
            $"Dimensions: {parameters.Width}x{parameters.Height}\n" +
            $"Format: {parameters.OutputFormat}\n" +
            $"Quality: {parameters.OutputQuality}\n" +
            $"Upsampling: {parameters.PromptUpsampling}";

        image.Metadata.ExifProfile ??= new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.UserComment, metadataText);

        var encoder = GetImageEncoder(parameters.OutputFormat, parameters.OutputQuality);
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

    private static IImageEncoder GetImageEncoder(ImageOutputFormat format, int quality)
    {
        return format switch
        {
            ImageOutputFormat.Jpg => new JpegEncoder { Quality = quality },
            ImageOutputFormat.Webp => new WebpEncoder { Quality = quality },
            ImageOutputFormat.Png or _ => new PngEncoder(),
        };
    }
}
