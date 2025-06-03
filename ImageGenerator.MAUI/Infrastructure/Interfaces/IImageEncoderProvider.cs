using ImageGenerator.MAUI.Core.Domain.Enums;
using SixLabors.ImageSharp.Formats;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Defines an interface for providing image encoders based on the output format and quality settings.
/// </summary>
/// <remarks>
/// Implementations of this interface should return an appropriate image encoder for the
/// specified <see cref="ImageOutputFormat"/> and quality. This allows customization of
/// the encoding process for various image formats such as JPEG, PNG, or WebP.
/// The quality parameter can be used to specify the desired encoding quality if applicable
/// to the format in use.
/// </remarks>
public interface IImageEncoderProvider
{
    /// <summary>
    /// Returns an image encoder instance based on the specified output format and quality settings.
    /// </summary>
    /// <param name="format">The desired image output format (e.g., Jpg, Png, Webp).</param>
    /// <param name="quality">The quality level for the encoder, applicable for formats supporting compression (e.g., Jpg, Webp).</param>
    /// <returns>An instance of <see cref="IImageEncoder"/> tailored to the specified format and quality settings.</returns>
    IImageEncoder GetImageEncoder(ImageOutputFormat format, int quality);
}
