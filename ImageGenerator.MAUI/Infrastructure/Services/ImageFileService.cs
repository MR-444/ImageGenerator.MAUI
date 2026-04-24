using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public class ImageFileService : IImageFileService
{
    private readonly IImageEncoderProvider _encoderProvider;

    public ImageFileService(IImageEncoderProvider encoderProvider)
    {
        _encoderProvider = encoderProvider;
    }

    public async Task SaveImageWithMetadataAsync(string imagePath, byte[] imageBytes, ImageGenerationParameters parameters)
    {
        // Snapshot all parameter reads before the first async yield — defence-in-depth even
        // though callers now pass a cloned instance (the original race was the user editing
        // the prompt while image bytes were loading).
        var prompt = parameters.Prompt;
        var model = parameters.Model ?? string.Empty;
        var seed = parameters.Seed;
        var aspectRatio = parameters.AspectRatio;
        var outputFormat = parameters.OutputFormat;
        var outputQuality = parameters.OutputQuality;
        var upsampling = parameters.PromptUpsampling;
        var gptQuality = parameters.GptQuality;
        var gptBackground = parameters.GptBackground;
        var gptModeration = parameters.GptModeration;
        var gptInputFidelity = parameters.GptInputFidelity;
        var resolution = parameters.Resolution;
        var raw = parameters.Raw;
        var imagePromptStrength = parameters.ImagePromptStrength;

        using var image = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(new MemoryStream(imageBytes));

        // Actual pixel dimensions — Parameters.Width/Height are only meaningful for models
        // that size via explicit width/height (Flux with custom dimensions). For aspect_ratio
        // models (gpt-image-*, nano-banana, Flux presets) they reflect the UI control, not the
        // produced image, and would be misleading.
        var lines = new List<string>
        {
            $"Prompt: {prompt}",
            $"ModelName: {model}",
            $"Seed: {seed}",
            $"AspectRatio: {aspectRatio}",
            $"Dimensions: {image.Width}x{image.Height}",
            $"Format: {outputFormat}",
            $"Quality: {outputQuality}",
        };

        if (model.Contains("flux-1.1-pro", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"Upsampling: {upsampling}");
        }
        if (model.Contains("flux-1.1-pro-ultra", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"Raw: {raw}");
            lines.Add($"ImagePromptStrength: {imagePromptStrength}");
        }
        if (model.Contains("gpt-image", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"GptQuality: {gptQuality}");
            lines.Add($"GptBackground: {gptBackground}");
            lines.Add($"GptModeration: {gptModeration}");
            lines.Add($"GptInputFidelity: {gptInputFidelity}");
        }
        if (model.Contains("nano-banana", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"Resolution: {resolution}");
        }

        var metadataText = string.Join(Environment.NewLine, lines);

        // Use each format's native metadata mechanism and drop anything the API source
        // may have embedded, so the prompt never shows up twice in a viewer.
        if (outputFormat == ImageOutputFormat.Png)
        {
            image.Metadata.ExifProfile = null;
            var pngMeta = image.Metadata.GetPngMetadata();
            pngMeta.TextData.Clear();
            pngMeta.TextData.Add(new PngTextData("Comment", metadataText, "en", string.Empty));
        }
        else
        {
            // EncodedString writes the 8-byte UNICODE\0 charset prefix required by the EXIF 2.3 spec
            // so third-party readers (IrfanView, Explorer Properties) see a proper UTF-16 string.
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(
                ExifTag.UserComment,
                new EncodedString(EncodedString.CharacterCode.Unicode, metadataText));
        }

        var encoder = _encoderProvider.GetImageEncoder(outputFormat, outputQuality);
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

    public string GetUniqueSavePath(string directory, ImageGenerationParameters parameters)
    {
        var baseName = BuildFileName(parameters);
        var candidate = Path.Combine(directory, baseName);
        if (!File.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);
        for (var i = 1; i < 10_000; i++)
        {
            var next = Path.Combine(directory, $"{stem}_{i}{ext}");
            if (!File.Exists(next)) return next;
        }
        throw new IOException($"Could not find an unused filename for '{baseName}' in '{directory}' after 10000 attempts.");
    }
}
