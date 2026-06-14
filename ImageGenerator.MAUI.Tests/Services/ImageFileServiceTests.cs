using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageGenerator.MAUI.Tests.Services;

public class ImageFileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImageFileService _sut;

    public ImageFileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imggen-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        // Frozen clock so collision-suffix tests don't race the wall-clock second boundary.
        var frozen = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        _sut = new ImageFileService(new ImageEncoderProvider(), ModelDescriptorRegistry.Default(), () => frozen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static ImageGenerationParameters SampleParameters() => new()
    {
        ApiToken = "t",
        Model = "black-forest-labs/flux-dev",
        Prompt = "a cat",
        Seed = 42,
        OutputFormat = ImageOutputFormat.Png,
        OutputQuality = 90
    };

    [Fact]
    public void BuildFileName_ProducesTimestampPromptSeedFormat()
    {
        var name = _sut.BuildFileName(SampleParameters());

        name.Should().EndWith("_a_cat_42.png");
        Path.GetInvalidFileNameChars().Any(c => name.Contains(c)).Should().BeFalse();
    }

    [Fact]
    public void GetUniqueSavePath_WhenNoFileExists_ReturnsBaseName()
    {
        var path = _sut.GetUniqueSavePath(_tempDir, SampleParameters());

        File.Exists(path).Should().BeFalse();
        Path.GetDirectoryName(path).Should().Be(_tempDir);
    }

    [Fact]
    public void GetUniqueSavePath_WhenBaseExists_AppendsSuffix()
    {
        var parameters = SampleParameters();
        var first = _sut.GetUniqueSavePath(_tempDir, parameters);
        File.WriteAllBytes(first, [1, 2, 3]);

        var second = _sut.GetUniqueSavePath(_tempDir, parameters);

        second.Should().NotBe(first);
        Path.GetFileNameWithoutExtension(second).Should().EndWith("_1");
        File.Exists(second).Should().BeFalse();
    }

    [Fact]
    public void GetUniqueSavePath_WhenSeveralSuffixesTaken_ReturnsNextFreeIndex()
    {
        var parameters = SampleParameters();
        var baseName = _sut.BuildFileName(parameters);
        var stem = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);

        File.WriteAllBytes(Path.Combine(_tempDir, baseName), [1]);
        File.WriteAllBytes(Path.Combine(_tempDir, $"{stem}_1{ext}"), [1]);
        File.WriteAllBytes(Path.Combine(_tempDir, $"{stem}_2{ext}"), [1]);
        File.WriteAllBytes(Path.Combine(_tempDir, $"{stem}_3{ext}"), [1]);

        var next = _sut.GetUniqueSavePath(_tempDir, parameters);

        Path.GetFileNameWithoutExtension(next).Should().EndWith("_4");
        File.Exists(next).Should().BeFalse();
    }

    [Fact]
    public async Task SaveImageWithMetadataAsync_ForJpg_WritesExifUserCommentOnly()
    {
        var parameters = SampleParameters();
        parameters.Prompt = "a round-trip cat";
        parameters.Seed = 42;
        parameters.OutputFormat = ImageOutputFormat.Jpg;

        var bytes = Build1x1Image(ImageOutputFormat.Jpg);
        var path = _sut.GetUniqueSavePath(_tempDir, parameters);

        await _sut.SaveImageWithMetadataAsync(path, bytes, parameters);

        File.Exists(path).Should().BeTrue();
        using var saved = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(path);
        saved.Metadata.ExifProfile.Should().NotBeNull();
        saved.Metadata.ExifProfile!.TryGetValue(ExifTag.UserComment, out var comment).Should().BeTrue();
        comment!.Value.Text.Should().Contain("Prompt: a round-trip cat").And.Contain("Seed: 42");
    }

    [Fact]
    public async Task SaveImageWithMetadataAsync_ForPng_WritesTextChunkAndNoExifUserComment()
    {
        var parameters = SampleParameters();
        parameters.Prompt = "a round-trip cat";
        parameters.Seed = 42;
        parameters.OutputFormat = ImageOutputFormat.Png;

        var bytes = Build1x1Image(ImageOutputFormat.Png);
        var path = _sut.GetUniqueSavePath(_tempDir, parameters);

        await _sut.SaveImageWithMetadataAsync(path, bytes, parameters);

        File.Exists(path).Should().BeTrue();
        using var saved = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(path);

        var pngMeta = saved.Metadata.GetPngMetadata();
        pngMeta.TextData.Should().ContainSingle(t => t.Keyword == "Comment");
        var comment = pngMeta.TextData.Single(t => t.Keyword == "Comment");
        comment.Value.Should().Contain("Prompt: a round-trip cat").And.Contain("Seed: 42");

        // Viewers must not see a duplicate copy via EXIF.
        if (saved.Metadata.ExifProfile is not null)
        {
            saved.Metadata.ExifProfile.TryGetValue(ExifTag.UserComment, out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task SaveImageWithMetadataAsync_FluxProUltra_DoesNotEmitUpsamplingLine()
    {
        // Regression: a substring `model.Contains("flux-1.1-pro")` would match
        // `flux-1.1-pro-ultra` too, attaching a misleading `Upsampling:` line to Ultra images.
        var parameters = SampleParameters();
        parameters.Model = "black-forest-labs/flux-1.1-pro-ultra";
        parameters.PromptUpsampling = false;
        parameters.Raw = true;
        parameters.OutputFormat = ImageOutputFormat.Png;

        var bytes = Build1x1Image(ImageOutputFormat.Png);
        var path = _sut.GetUniqueSavePath(_tempDir, parameters);

        await _sut.SaveImageWithMetadataAsync(path, bytes, parameters);

        using var saved = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(path);
        var pngMeta = saved.Metadata.GetPngMetadata();
        var comment = pngMeta.TextData.Single(t => t.Keyword == "Comment").Value;

        comment.Should().NotContain("Upsampling:");
        comment.Should().Contain("Raw:");
        comment.Should().Contain("ImagePromptStrength:");
    }

    [Fact]
    public async Task SaveImageWithMetadataAsync_ForPng_PreservesOriginalPixelStreamByteForByte()
    {
        // The fast path must splice metadata without re-encoding — the provider's exact IDAT
        // (compressed pixel) stream has to survive untouched so CivitAI uploads stay faithful.
        var parameters = SampleParameters();
        var bytes = BuildContentPng(8, 8);
        var path = _sut.GetUniqueSavePath(_tempDir, parameters);

        await _sut.SaveImageWithMetadataAsync(path, bytes, parameters);

        var saved = await File.ReadAllBytesAsync(path);
        ExtractIdat(saved).Should().Equal(ExtractIdat(bytes));
    }

    [Fact]
    public async Task SaveImageWithMetadataAsync_ForPng_UnicodePromptRoundTripsThroughITxt()
    {
        var parameters = SampleParameters();
        parameters.Prompt = "café 🐱 日本語";
        var bytes = BuildContentPng(4, 4);
        var path = _sut.GetUniqueSavePath(_tempDir, parameters);

        await _sut.SaveImageWithMetadataAsync(path, bytes, parameters);

        using var saved = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(path);
        var comment = saved.Metadata.GetPngMetadata().TextData.Single(t => t.Keyword == "Comment").Value;
        comment.Should().Contain("Prompt: café 🐱 日本語");
    }

    [Fact]
    public async Task SaveImageWithMetadataAsync_MutationVariantCaption_SurvivesPromotionRoundTrip()
    {
        // Promotion guard for the mutation engine: a low-setting variant's compact structured
        // caption must survive write→read so a winner can later be remixed / re-mutated. The
        // metadata block is a Key: Value line list, so ONLY single-line text survives — variant
        // captions are compact (single-line), which is exactly why this works. Reads back through
        // the REAL GalleryService.ReadMetadataAsync — the same reader the promotion path uses.
        var engine = new CaptionMutationEngine();
        var config = new MutationRunConfig
        {
            Axis = MutationAxis.Look,
            Count = 3,
            Seed = 1234,
            TargetWidth = 1024,
            TargetHeight = 1024,
            IncludeBaseAsReference = false,
            Strength = MutationStrength.Subtle
        };
        var result = engine.Generate(MutationTestData.BaseCaption(), config, MutationTestData.Library());
        result.Variants.Should().NotBeEmpty();

        var caption = result.Variants[0].Caption;
        caption.Should().NotContain("\n"); // compact / single-line — else the metadata block truncates it
        _ = V4JsonPromptSerializer.Deserialize(caption); // engine emits validator-clean captions

        var parameters = SampleParameters();
        parameters.Prompt = caption;
        var bytes = BuildContentPng(8, 8);
        var path = _sut.GetUniqueSavePath(_tempDir, parameters);

        await _sut.SaveImageWithMetadataAsync(path, bytes, parameters);

        var meta = await new GalleryService().ReadMetadataAsync(path);
        meta.Should().NotBeNull();
        meta!["Prompt"].Should().Be(caption);

        // Structured equivalence: both sides parse to the same canonical compact serialization.
        V4JsonPromptSerializer.Serialize(V4JsonPromptSerializer.Deserialize(meta["Prompt"]))
            .Should().Be(V4JsonPromptSerializer.Serialize(V4JsonPromptSerializer.Deserialize(caption)));
    }

    [Fact]
    public async Task SaveImageWithMetadataAsync_ForPng_WithExistingComment_DeduplicatesToOne()
    {
        // A provider PNG that already carries a Comment chunk must not produce a second copy.
        var parameters = SampleParameters();
        var bytes = BuildContentPng(4, 4, existingComment: "Prompt: stale");
        var path = _sut.GetUniqueSavePath(_tempDir, parameters);

        await _sut.SaveImageWithMetadataAsync(path, bytes, parameters);

        using var saved = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(path);
        var comments = saved.Metadata.GetPngMetadata().TextData.Where(t => t.Keyword == "Comment").ToList();
        comments.Should().ContainSingle();
        comments[0].Value.Should().Contain("Prompt: a cat").And.NotContain("stale");
    }

    [Fact]
    public async Task SaveImageWithMetadataAsync_PngRequestedButSourceIsJpeg_FallsBackAndTranscodes()
    {
        // Defensive fallback: a provider returning JPEG bytes for a PNG request must still yield
        // a valid PNG carrying the Comment chunk (the decode+re-encode path, not the splice).
        var parameters = SampleParameters();
        parameters.OutputFormat = ImageOutputFormat.Png;
        var jpegBytes = Build1x1Image(ImageOutputFormat.Jpg);
        var path = _sut.GetUniqueSavePath(_tempDir, parameters);

        await _sut.SaveImageWithMetadataAsync(path, jpegBytes, parameters);

        using var saved = await SixLabors.ImageSharp.Image.LoadAsync<Rgba32>(path);
        saved.Metadata.GetPngMetadata().TextData.Should().ContainSingle(t => t.Keyword == "Comment");
    }

    private static byte[] Build1x1Image(ImageOutputFormat format)
    {
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(1, 1);
        using var memory = new MemoryStream();
        if (format == ImageOutputFormat.Png) image.Save(memory, new PngEncoder());
        else image.Save(memory, new JpegEncoder());
        return memory.ToArray();
    }

    private static byte[] BuildContentPng(int width, int height, string? existingComment = null)
    {
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                image[x, y] = new Rgba32((byte)(x * 17), (byte)(y * 23), (byte)((x + y) * 7), 255);

        if (existingComment is not null)
            image.Metadata.GetPngMetadata().TextData.Add(new PngTextData("Comment", existingComment, "en", string.Empty));

        using var memory = new MemoryStream();
        image.Save(memory, new PngEncoder());
        return memory.ToArray();
    }

    // Concatenated data of every IDAT chunk — the compressed pixel stream.
    private static byte[] ExtractIdat(byte[] png)
    {
        using var ms = new MemoryStream();
        var offset = 8; // skip signature
        while (offset + 8 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT") ms.Write(png, offset + 8, length);
            offset += 12 + length;
        }
        return ms.ToArray();
    }

    [Fact]
    public void GetUniqueSavePath_WhenSeveralExist_AppendsNextAvailable()
    {
        var parameters = SampleParameters();
        var first = _sut.GetUniqueSavePath(_tempDir, parameters);
        File.WriteAllBytes(first, [0]);

        var second = _sut.GetUniqueSavePath(_tempDir, parameters);
        File.WriteAllBytes(second, [0]);

        var third = _sut.GetUniqueSavePath(_tempDir, parameters);
        File.WriteAllBytes(third, [0]);

        var fourth = _sut.GetUniqueSavePath(_tempDir, parameters);

        Path.GetFileNameWithoutExtension(second).Should().EndWith("_1");
        Path.GetFileNameWithoutExtension(third).Should().EndWith("_2");
        Path.GetFileNameWithoutExtension(fourth).Should().EndWith("_3");
    }
}
