using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Services;

namespace ImageGenerator.MAUI.Tests.Services;

public class ImageDataUriEncoderTests
{
    [Fact]
    public void BuildDataUri_AlreadyDataUriPrefixed_ReturnsUnchanged()
    {
        const string already = "data:image/png;base64,iVBORw0KGgo=";

        var result = ImageDataUriEncoder.BuildDataUri(already);

        result.Should().Be(already);
    }

    [Fact]
    public void BuildDataUri_PngMagicBytes_ProducesPngDataUri()
    {
        var base64 = ToBase64(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D);

        var result = ImageDataUriEncoder.BuildDataUri(base64);

        result.Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public void BuildDataUri_JpegMagicBytes_ProducesJpegDataUri()
    {
        var base64 = ToBase64(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01);

        var result = ImageDataUriEncoder.BuildDataUri(base64);

        result.Should().StartWith("data:image/jpeg;base64,");
    }

    [Fact]
    public void BuildDataUri_GifMagicBytes_ProducesGifDataUri()
    {
        var base64 = ToBase64(0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00);

        var result = ImageDataUriEncoder.BuildDataUri(base64);

        result.Should().StartWith("data:image/gif;base64,");
    }

    [Fact]
    public void BuildDataUri_WebpMagicBytes_ProducesWebpDataUri()
    {
        // RIFF + 4 size bytes + WEBP at offsets 8-11
        var base64 = ToBase64(0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50);

        var result = ImageDataUriEncoder.BuildDataUri(base64);

        result.Should().StartWith("data:image/webp;base64,");
    }

    [Fact]
    public void BuildDataUri_GarbageBase64_FallsBackToPngMime()
    {
        // Asterisks aren't valid base64; TryFromBase64Chars returns false → png fallback.
        var result = ImageDataUriEncoder.BuildDataUri("****************");

        result.Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public void BuildDataUri_TooShortToMatchAnyMagic_FallsBackToPngMime()
    {
        // 4 base64 chars decode to 3 zero bytes — no signature matches.
        var result = ImageDataUriEncoder.BuildDataUri("AAAA");

        result.Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public void BuildDataUris_EmptyCollection_ReturnsNull()
    {
        var result = ImageDataUriEncoder.BuildDataUris([], maxCount: 5);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildDataUris_TruncatesToMaxCount()
    {
        var inputs = new[] { "AAAA", "AAAA", "AAAA", "AAAA" };

        var result = ImageDataUriEncoder.BuildDataUris(inputs, maxCount: 2);

        result.Should().NotBeNull().And.HaveCount(2);
    }

    private static string ToBase64(params byte[] bytes) => Convert.ToBase64String(bytes);
}
