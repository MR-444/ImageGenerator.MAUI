using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
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

    private static byte[] Build1x1Image(ImageOutputFormat format)
    {
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(1, 1);
        using var memory = new MemoryStream();
        if (format == ImageOutputFormat.Png) image.Save(memory, new PngEncoder());
        else image.Save(memory, new JpegEncoder());
        return memory.ToArray();
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
