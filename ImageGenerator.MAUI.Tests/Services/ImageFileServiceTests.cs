using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Infrastructure.Services;

namespace ImageGenerator.MAUI.Tests.Services;

public class ImageFileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImageFileService _sut;

    public ImageFileServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imggen-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sut = new ImageFileService(new ImageEncoderProvider());
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
