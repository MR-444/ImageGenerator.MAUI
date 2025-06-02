using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Services;

public class ImageEncoderProvider : IImageEncoderProvider
{
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
