using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Services;

/// <summary>
/// Provides functionality for retrieving an appropriate image encoder based on the specified image format and quality.
/// </summary>
/// <remarks>
/// The <see cref="ImageEncoderProvider"/> class implements the <see cref="IImageEncoderProvider"/> interface
/// to return specific image encoders, such as JpegEncoder, WebpEncoder, or PngEncoder, depending on the provided
/// <see cref="ImageOutputFormat"/>. This class supports customization of encoding quality where applicable.
/// </remarks>
public class ImageEncoderProvider : IImageEncoderProvider
{
    /// <summary>
    /// Returns an image encoder instance based on the specified output format and quality settings.
    /// </summary>
    /// <param name="format">The desired image output format (e.g., Jpg, Png, Webp).</param>
    /// <param name="quality">The quality level for the encoder, applicable for formats supporting compression (e.g., Jpg, Webp).</param>
    /// <returns>An instance of <see cref="IImageEncoder"/> tailored to the specified format and quality settings.</returns>
    public IImageEncoder GetImageEncoder(ImageOutputFormat format, int quality)
    {
        return format switch
        {
            ImageOutputFormat.Jpg => new JpegEncoder { Quality = quality },
            ImageOutputFormat.Webp => new WebpEncoder { Quality = quality },
            ImageOutputFormat.Png or _ => new PngEncoder(),
        };
    }
}
