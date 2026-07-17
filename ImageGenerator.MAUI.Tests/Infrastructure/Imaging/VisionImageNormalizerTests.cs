using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Imaging;

public class VisionImageNormalizerTests
{
    private static byte[] Encode(Action<Image<Rgba32>, MemoryStream> save)
    {
        using var image = new Image<Rgba32>(2, 2);
        using var ms = new MemoryStream();
        save(image, ms);
        return ms.ToArray();
    }

    [Fact]
    public void Png_PassesThroughByteIdentical()
    {
        var png = Encode((i, ms) => i.SaveAsPng(ms));

        var (bytes, error) = VisionImageNormalizer.Normalize(png);

        error.Should().BeNull();
        bytes.Should().BeSameAs(png, "PNG needs no work — pass-through, not a re-encode");
    }

    [Fact]
    public void Jpeg_PassesThroughByteIdentical()
    {
        // Covers ".jfif" too: JFIF is plain JPEG bytes under a different extension, and the
        // normalizer sniffs bytes, never names.
        var jpeg = Encode((i, ms) => i.SaveAsJpeg(ms));

        var (bytes, error) = VisionImageNormalizer.Normalize(jpeg);

        error.Should().BeNull();
        bytes.Should().BeSameAs(jpeg);
    }

    [Fact]
    public void Webp_IsTranscodedToPng()
    {
        var webp = Encode((i, ms) => i.SaveAsWebp(ms));

        var (bytes, error) = VisionImageNormalizer.Normalize(webp);

        error.Should().BeNull();
        bytes!.Take(4).Should().Equal((byte)0x89, (byte)'P', (byte)'N', (byte)'G');
    }

    [Fact]
    public void Bmp_IsTranscodedToPng()
    {
        var bmp = Encode((i, ms) => i.SaveAsBmp(ms));

        var (bytes, error) = VisionImageNormalizer.Normalize(bmp);

        error.Should().BeNull();
        bytes!.Take(4).Should().Equal((byte)0x89, (byte)'P', (byte)'N', (byte)'G');
    }

    [Fact]
    public void UndecodableBytes_ReturnAUserFacingError()
    {
        var (bytes, error) = VisionImageNormalizer.Normalize([1, 2, 3, 4]);

        bytes.Should().BeNull();
        error.Should().Contain("Unsupported image format");
    }

    [Fact]
    public void EmptyBytes_ReturnAnError()
    {
        var (bytes, error) = VisionImageNormalizer.Normalize([]);

        bytes.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }
}
