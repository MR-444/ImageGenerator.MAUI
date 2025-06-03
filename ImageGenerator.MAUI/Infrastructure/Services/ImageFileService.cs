using ImageGenerator.MAUI.Infrastructure.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Infrastructure.Services;

/// <summary>
/// Provides services for handling image file operations,
/// including saving images with metadata and generating file names based on image generation parameters.
/// </summary>
public class ImageFileService : IImageFileService
{
    /// <summary>
    /// Provides an instance of <see cref="IImageEncoderProvider"/> used to select and configure
    /// appropriate image encoders based on specified output format and quality.
    /// </summary>
    /// <remarks>
    /// This variable is integral in the image saving process, as it supplies an encoder configured
    /// to meet the requirements defined by the user's parameters (e.g., format, quality).
    /// It plays a critical role in ensuring images are saved with the appropriate encoding.
    /// </remarks>
    private readonly IImageEncoderProvider _encoderProvider;

    /// <summary>
    /// A service for handling image file operations, including saving images with metadata
    /// and constructing file names based on image generation parameters.
    /// </summary>
    public ImageFileService(IImageEncoderProvider encoderProvider)
    {
        _encoderProvider = encoderProvider;
    }

    /// <summary>
    /// Saves an image to the specified file path with embedded metadata based on the provided
    /// image generation parameters.
    /// </summary>
    /// <param name="imagePath">The file path where the image will be saved.</param>
    /// <param name="imageBytes">The binary data of the image to be saved.</param>
    /// <param name="parameters">The parameters used for image generation, which will be included in the image metadata.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
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

    /// <summary>
    /// Constructs a file name for an image file based on the given image generation parameters.
    /// </summary>
    /// <param name="parameters">The parameters containing the data used to generate the file name,
    /// including prompt, seed, and output format.</param>
    /// <returns>A string representing a safe and formatted file name with a timestamp, truncated prompt,
    /// seed, and file extension derived from the parameters.</returns>
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
