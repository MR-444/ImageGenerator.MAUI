using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Imaging;

/// <summary>
/// The shared shrink-what-leaves-the-app rule: pass through what is already fine, transcode and
/// downscale what is not, refuse what cannot be made small. Tested one budget at a time — Vision for
/// the pixel cap, CivitAI's for bytes only — because the point of the shared code is that the
/// numbers are the only thing that differs between destinations.
/// </summary>
public class ImagePayloadNormalizerTests
{
    private static readonly ImagePayloadLimits Vision = ImagePayloadLimits.Vision;

    private static byte[] Encode(Action<Image<Rgba32>, MemoryStream> save)
    {
        using var image = new Image<Rgba32>(2, 2);
        using var ms = new MemoryStream();
        save(image, ms);
        return ms.ToArray();
    }

    private static bool IsJpeg(byte[]? bytes) =>
        bytes is { Length: > 3 } && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    private static bool IsPng(byte[]? bytes) =>
        bytes is { Length: > 4 } && bytes.Take(4).SequenceEqual([(byte)0x89, (byte)'P', (byte)'N', (byte)'G']);

    /// <summary>
    /// Photographic content: smooth colour with grain, so PNG pays for every pixel while JPEG's
    /// DCT does not — which is the difference the byte budget is written against. Pure noise would
    /// rather the other way round, and is used where the point is a file that cannot be saved.
    /// </summary>
    private static byte[] Photograph(int width, int height, int grain, int seed)
    {
        var random = new Random(seed);
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var grainOffset = random.Next(-grain, grain + 1);
                    row[x] = new Rgba32(
                        (byte)Math.Clamp((x * 255 / width) + grainOffset, 0, 255),
                        (byte)Math.Clamp((y * 255 / accessor.Height) + grainOffset, 0, 255),
                        (byte)Math.Clamp(((x + y) * 255 / (width + accessor.Height)) + grainOffset, 0, 255),
                        byte.MaxValue);
                }
            }
        });
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>Incompressible opaque pixels: noise, so a byte budget actually binds.</summary>
    private static byte[] Noise(int width, int height, int seed)
    {
        var random = new Random(seed);
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(
                        (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), byte.MaxValue);
                }
            }
        });
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    // ---- Pass-through ---------------------------------------------------------------------

    [Fact]
    public void Png_PassesThroughByteIdentical()
    {
        var png = Encode((i, ms) => i.SaveAsPng(ms));

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(png, Vision);

        error.Should().BeNull();
        note.Should().BeNull("nothing was done to it");
        bytes.Should().BeSameAs(png, "a payload inside the budgets is passed through, not re-encoded");
    }

    [Fact]
    public void Jpeg_PassesThroughByteIdentical()
    {
        // Covers ".jfif" too: JFIF is plain JPEG bytes under a different extension, and the
        // normalizer sniffs bytes, never names.
        var jpeg = Encode((i, ms) => i.SaveAsJpeg(ms));

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(jpeg, Vision);

        error.Should().BeNull();
        note.Should().BeNull();
        bytes.Should().BeSameAs(jpeg);
    }

    [Fact]
    public void JpegWithinDimensionCap_ButOverTheByteBudget_IsReEncoded()
    {
        // 800×800 is inside every dimension this app caps at, and a grainy photograph is the file
        // type that grows absurdly as PNG while JPEG shrugs it off — what the byte budget is for.
        var photo = Photograph(800, 800, grain: 12, seed: 7);
        photo.Length.Should().BeGreaterThan(400 * 1024, "the case under test is a file over its budget");
        var limits = Vision with { MaxBytes = 400 * 1024 };

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(photo, limits);

        error.Should().BeNull($"an 800×800 photograph fits 400 KB once it stops being a PNG: {error}");
        bytes!.Length.Should().BeLessThan(photo.Length);
        bytes.Length.Should().BeLessThanOrEqualTo(limits.MaxBytes);
        note.Should().NotBeNull().And.Contain("→");
    }

    [Fact]
    public void DestinationReadingMoreFormats_StillGetsAnUnreadableSmallFile()
    {
        // CivitAI decodes things this app cannot, and rejecting a 400 KB file it would happily
        // accept would be the app inventing a limitation. Only an oversize unreadable file is
        // refused, because reading it is the only way to shrink it.
        var notQuiteAnImage = new byte[64];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(notQuiteAnImage, 0);

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(notQuiteAnImage, ImagePayloadLimits.CivitAiUpload);

        error.Should().BeNull();
        bytes.Should().BeSameAs(notQuiteAnImage);

        var (_, _, visionError) = ImagePayloadNormalizer.Prepare(notQuiteAnImage, Vision);
        visionError.Should().Contain("Unsupported image format",
            "but Ollama takes nothing else, so it is refused there at once");
    }

    [Fact]
    public void CorruptPayloadWithValidSignature_IsRefusedEvenWithoutDimensionCap()
    {
        var notQuiteAnImage = new byte[64];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(notQuiteAnImage, 0);
        var noDimensionCapKnownFormat = new ImagePayloadLimits(
            MaxBytes: 1024 * 1024, MaxDimension: null, RequiresKnownFormat: true);

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(notQuiteAnImage, noDimensionCapKnownFormat);

        bytes.Should().BeNull();
        error.Should().Contain("Unsupported image format");
    }

    // ---- Transcoding ----------------------------------------------------------------------

    [Fact]
    public void Webp_IsTranscodedToPng()
    {
        var webp = Encode((i, ms) => i.SaveAsWebp(ms));

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(webp, Vision);

        error.Should().BeNull();
        IsPng(bytes).Should().BeTrue("Ollama only decodes JPEG and PNG");
    }

    [Fact]
    public void Bmp_IsTranscodedToJpeg()
    {
        // BMP carries no alpha at all, so there is nothing to protect and JPEG is the honest
        // answer — which is also why the format alone never decides, the pixels do.
        var bmp = Encode((i, ms) => i.SaveAsBmp(ms));

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(bmp, Vision);

        error.Should().BeNull();
        IsJpeg(bytes).Should().BeTrue();
    }

    [Fact]
    public void AnimatedGif_IsReducedToFirstFrame()
    {
        using var gif = new Image<Rgba32>(8, 8, new Rgba32(255, 0, 0));
        using var frame2 = new Image<Rgba32>(8, 8, new Rgba32(0, 0, 255));
        gif.Frames.AddFrame(frame2.Frames.RootFrame);
        using var ms = new MemoryStream();
        gif.SaveAsGif(ms);

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(ms.ToArray(), Vision);

        error.Should().BeNull();
        using var result = SixLabors.ImageSharp.Image.Load(bytes!);
        result.Frames.Count.Should().Be(1);
    }

    [Fact]
    public void SmallFile_IsNeverGrownToTheCap()
    {
        // A 2×2 WebP needs a new container, not an upscale. Growing whatever fits under the cap to
        // the cap is what ImageSharp's Max mode does by default, and it would send a hosted model
        // a quarter of a million times the pixels it was asked about, billed as tokens.
        var webp = Encode((i, ms) => i.SaveAsWebp(ms));

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(webp, Vision);

        error.Should().BeNull();
        using var result = SixLabors.ImageSharp.Image.Load(bytes!);
        result.Width.Should().Be(2);
        result.Height.Should().Be(2);
        note.Should().NotContain("×");
    }

    [Fact]
    public void TransparentSource_KeepsItsAlphaInsteadOfLosingItToAJpeg()
    {
        using var image = new Image<Rgba32>(1600, 1200);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(ms.ToArray(), Vision);

        error.Should().BeNull();
        note.Should().Contain("PNG");
        using var result = SixLabors.ImageSharp.Image.Load<Rgba32>(bytes!);
        result.Frames.RootFrame[0, 0].A.Should().Be(0, "flattening onto black invents pixels");
    }

    [Fact]
    public void PartialAlphaPixel_KeepsPngInsteadOfDroppingTransparency()
    {
        // Partial alpha, not just fully transparent: a JPEG encoder flattening a half-visible pixel
        // onto black invents pixels the user never had, so the decision reads the pixels and stops
        // at the first one below full opacity. The soft corner is a region rather than a lone pixel
        // because ImageSharp's lossy WebP encoder throws the alpha plane away for a nearly-opaque
        // image (its output here decodes as plain RGB even in libwebp), and a single pixel would
        // have this test assert on transparency no encoder ever wrote.
        using var image = new Image<Rgba32>(20, 20, new Rgba32(255, 255, 255, 255));
        image.ProcessPixelRows(accessor =>
        {
            for (var y = accessor.Height - 6; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = row.Length - 6; x < row.Length; x++)
                {
                    row[x] = new Rgba32(100, 150, 200, 128);
                }
            }
        });
        using var ms = new MemoryStream();
        image.SaveAsWebp(ms);

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(ms.ToArray(), Vision);

        error.Should().BeNull();
        note.Should().Contain("PNG");
        IsPng(bytes).Should().BeTrue();
        using var result = SixLabors.ImageSharp.Image.Load<Rgba32>(bytes!);
        result.Frames.RootFrame[19, 19].A.Should().BeLessThan(byte.MaxValue);
    }

    [Fact]
    public void OpaqueSource_BecomesJpeg()
    {
        using var image = new Image<Rgba32>(1600, 1200, new Rgba32(255, 255, 255));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(ms.ToArray(), Vision);

        error.Should().BeNull();
        note.Should().Contain("JPEG");
        bytes!.Take(3).Should().Equal(0xFF, 0xD8, 0xFF);
    }

    // ---- Downscaling ----------------------------------------------------------------------

    [Fact]
    public void OversizedPng_IsDownscaledToMaxDimension()
    {
        using var image = new Image<Rgba32>(2000, 1000, new Rgba32(255, 255, 255));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        var original = ms.ToArray();

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(original, Vision);

        error.Should().BeNull();
        bytes.Should().NotBeNull();
        bytes.Should().NotBeSameAs(original, "an oversized image has to be re-encoded, not passed through");

        using var resultImage = SixLabors.ImageSharp.Image.Load(bytes);
        resultImage.Width.Should().Be(Vision.MaxDimension!.Value);
        resultImage.Height.Should().Be(512); // Proportional: 1000 * (1024 / 2000) = 512
    }

    [Fact]
    public void OversizedJpeg_IsDownscaledToMaxDimension()
    {
        using var image = new Image<Rgba32>(1000, 2000, new Rgba32(255, 255, 255));
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        var original = ms.ToArray();

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(original, Vision);

        error.Should().BeNull();
        bytes.Should().NotBeNull();
        bytes.Should().NotBeSameAs(original);

        using var resultImage = SixLabors.ImageSharp.Image.Load(bytes);
        resultImage.Width.Should().Be(512);
        resultImage.Height.Should().Be(Vision.MaxDimension!.Value);
    }

    [Fact]
    public void ModelInputBudget_DownscalesLargePhotoTo2048AndFitsUnder2Mb()
    {
        var largePhoto = Photograph(3000, 2000, grain: 10, seed: 9);

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(largePhoto, ImagePayloadLimits.ModelInput);

        error.Should().BeNull();
        bytes!.Length.Should().BeLessThanOrEqualTo(ImagePayloadLimits.ModelInput.MaxBytes);
        note.Should().Contain("3000×2000 → 2048×1365");
        using var result = SixLabors.ImageSharp.Image.Load(bytes);
        result.Width.Should().Be(2048);
    }

    [Fact]
    public void OversizedAndNoisy_StepsDownUntilTheBudgetIsMet()
    {
        // 2600×2600 noise: over the cap, and over the budget on every rung until the smallest —
        // so this is the ladder stepping, not the first attempt happening to land.
        var noisy = Noise(2600, 2600, seed: 11);
        var limits = new ImagePayloadLimits(MaxBytes: 800 * 1024, MaxDimension: 2000);

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(noisy, limits);

        error.Should().BeNull();
        using var result = SixLabors.ImageSharp.Image.Load(bytes!);
        result.Width.Should().Be(768, "the budget only becomes reachable at the smallest step");
        bytes!.Length.Should().BeLessThanOrEqualTo(limits.MaxBytes);
    }

    [Fact]
    public void CivitAiUploadBudget_KeepsEveryPixel()
    {
        // CivitAI's rule: the resolution is the point of the file, only its size may change. The
        // budget is widened here to what one 2600×2600 noise file needs; the posting service tests
        // carry the real 10 MiB figure against a realistic upscaled render.
        var limits = ImagePayloadLimits.CivitAiUpload with { MaxBytes = 20 * 1024 * 1024 };
        var original = Noise(2600, 2600, seed: 5);
        original.Length.Should().BeGreaterThan(limits.MaxBytes, "the case under test is an upload over its cap");

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(original, limits);

        error.Should().BeNull();
        bytes!.Length.Should().BeLessThan(limits.MaxBytes);
        using var result = SixLabors.ImageSharp.Image.Load(bytes);
        result.Width.Should().Be(2600);
        result.Height.Should().Be(2600);
    }

    [Fact]
    public void OverBudgetAndRefusingToShrink_ReportsItInWords()
    {
        var original = Noise(600, 600, seed: 3);

        var (bytes, _, error) = ImagePayloadNormalizer.Prepare(original, new ImagePayloadLimits(MaxBytes: 1024));

        bytes.Should().BeNull();
        error.Should().NotBeNull()
            .And.Contain("KB", "the report names both sizes so the user knows how far off it is");
    }

    [Fact]
    public void Note_NamesTheSizeActuallyGoingOut()
    {
        var original = Photograph(2000, 1000, grain: 12, seed: 4);

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(original, Vision);

        error.Should().BeNull();
        using var result = SixLabors.ImageSharp.Image.Load(bytes!);
        note.Should().Contain("2000×1000 →").And.Contain($"{result.Width}×{result.Height}",
            "what the user reads has to be the picture that leaves");

        var (_, containerOnlyNote, _) = ImagePayloadNormalizer.Prepare(Encode((i, ms) => i.SaveAsBmp(ms)), Vision);
        containerOnlyNote.Should().NotContain("×", "nothing was resized, only the container changed");
    }

    // ---- Metadata -------------------------------------------------------------------------

    [Fact]
    public void ExifOrientation_IsBakedInAndTheTagsAreDropped_EvenWhenInsideBudgets()
    {
        // A phone photo is sideways in pixels and upright only in its EXIF tag. Even when its file
        // size and dimensions already fit the budgets, endpoints that ignore EXIF tags would see
        // a sideways photo — and receive the GPS coordinates in the same profile.
        using var image = new Image<Rgba32>(100, 50, new Rgba32(255, 255, 255));
        var exif = new ExifProfile();
        // Orientation 8 ("left-top"): the pixels are stored on their side, and only this tag says so.
        exif.SetValue(ExifTag.Orientation, (ushort)8);
        image.Metadata.ExifProfile = exif;
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);

        var (bytes, note, error) = ImagePayloadNormalizer.Prepare(
            ms.ToArray(), new ImagePayloadLimits(MaxBytes: 64 * 1024, MaxDimension: 200));

        error.Should().BeNull();
        note.Should().NotContain("×", "the image was already 50×100 visually, only rotated in-place");
        using var result = SixLabors.ImageSharp.Image.Load(bytes!);
        result.Width.Should().Be(50);
        result.Height.Should().Be(100,
            "LeftTop is a quarter turn the EXIF tag had merely asked for, and the pixels now obey it");
        result.Metadata.ExifProfile.Should().BeNull("GPS and device details stay at home");
    }

    // ---- Async & Rejections ---------------------------------------------------------------

    [Fact]
    public async Task PrepareAsync_RespectsCancellation()
    {
        var png = Encode((i, ms) => i.SaveAsPng(ms));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var token = cts.Token;

        await FluentActions
            .Awaiting(() => ImagePayloadNormalizer.PrepareAsync(png, Vision, token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void UndecodableBytes_ReturnAUserFacingError()
    {
        var (bytes, _, error) = ImagePayloadNormalizer.Prepare([1, 2, 3, 4], Vision);

        bytes.Should().BeNull();
        error.Should().Contain("Unsupported image format");
    }

    [Fact]
    public void EmptyBytes_ReturnAnError()
    {
        var (bytes, _, error) = ImagePayloadNormalizer.Prepare([], Vision);

        bytes.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }
}
