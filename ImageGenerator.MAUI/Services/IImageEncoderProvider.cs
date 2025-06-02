using SixLabors.ImageSharp.Formats;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Services;

public interface IImageEncoderProvider
{
    IImageEncoder GetImageEncoder(ImageOutputFormat format, int quality);
}
