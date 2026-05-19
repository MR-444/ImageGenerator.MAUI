using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services;

public class JobRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IImageGenerationService> _imageService = new();
    private readonly Mock<IImageFileService> _imageFileService = new();
    private readonly JobRunner _sut;

    public JobRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imggen-jobrunner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sut = new JobRunner(
            _imageService.Object,
            _imageFileService.Object,
            NullLogger<JobRunner>.Instance);
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
        Model = "black-forest-labs/flux-dev",
        Prompt = "a cat",
        Seed = 42,
        ApiToken = "t"
    };

    private string PathInTempDir(string name = "out.png") => Path.Combine(_tempDir, name);

    [Fact]
    public async Task RunAsync_ServiceReturnsImageBytes_SavesAndReturnsSavedOutcome()
    {
        var parameters = SampleParameters();
        var expectedPath = PathInTempDir();
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedImage { ImageData = new byte[] { 1, 2, 3 }, Message = "ok" });
        _imageFileService.Setup(f => f.GetUniqueSavePath(It.IsAny<string>(), parameters))
            .Returns(expectedPath);
        _imageFileService.Setup(f => f.SaveImageWithMetadataAsync(expectedPath, It.IsAny<byte[]>(), parameters))
            .Returns(Task.CompletedTask);

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        outcome.Kind.Should().Be(JobOutcomeKind.Saved);
        outcome.SavedPath.Should().Be(expectedPath);
        outcome.Message.Should().Contain(expectedPath);
    }

    [Fact]
    public async Task RunAsync_ServiceReturnsNullImageData_ReturnsFailedOutcome()
    {
        var parameters = SampleParameters();
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedImage { ImageData = null, Message = "service error" });

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        outcome.Kind.Should().Be(JobOutcomeKind.Failed);
        outcome.SavedPath.Should().BeNull();
        outcome.Message.Should().Be("service error");
        _imageFileService.Verify(
            f => f.SaveImageWithMetadataAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<ImageGenerationParameters>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_ServiceReturnsEmptyImageData_ReturnsFailedOutcome()
    {
        var parameters = SampleParameters();
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedImage { ImageData = Array.Empty<byte>(), Message = "empty" });

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        outcome.Kind.Should().Be(JobOutcomeKind.Failed);
        outcome.SavedPath.Should().BeNull();
        outcome.Message.Should().Be("empty");
    }

    [Fact]
    public async Task RunAsync_ServiceThrowsOperationCanceled_Propagates()
    {
        // The VM's RunJobAsync distinguishes OperationCanceledException from other failures, so
        // JobRunner must not catch it — let it bubble.
        var parameters = SampleParameters();
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await _sut.RunAsync(parameters, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_SaveThrows_DeletesPartialFileAndReturnsFailed()
    {
        // F7 regression: ImageSharp writes directly to the final path, so a mid-write failure
        // can leave a partial image on disk. JobRunner must best-effort delete it.
        var parameters = SampleParameters();
        var partialPath = PathInTempDir("partial.png");
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedImage { ImageData = new byte[] { 1, 2, 3 }, Message = "ok" });
        _imageFileService.Setup(f => f.GetUniqueSavePath(It.IsAny<string>(), parameters))
            .Returns(partialPath);
        _imageFileService.Setup(f => f.SaveImageWithMetadataAsync(partialPath, It.IsAny<byte[]>(), parameters))
            .Returns(async () =>
            {
                // Simulate the partial-write scenario: bytes hit disk, then the writer dies.
                await File.WriteAllBytesAsync(partialPath, new byte[] { 1, 2, 3 });
                throw new IOException("simulated AV lock");
            });

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        outcome.Kind.Should().Be(JobOutcomeKind.Failed);
        outcome.SavedPath.Should().BeNull();
        outcome.Message.Should().Contain("simulated AV lock");
        File.Exists(partialPath).Should().BeFalse("partial file must be cleaned up after save failure");
    }

    [Fact]
    public async Task RunAsync_SaveThrowsWithNoPartialFile_ReturnsFailedWithoutThrowing()
    {
        // If the save throws before any bytes hit disk (e.g. path-too-long check), the cleanup
        // branch must still execute cleanly (File.Exists returns false; no delete attempt).
        var parameters = SampleParameters();
        var targetPath = PathInTempDir("never-written.png");
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedImage { ImageData = new byte[] { 1, 2, 3 }, Message = "ok" });
        _imageFileService.Setup(f => f.GetUniqueSavePath(It.IsAny<string>(), parameters))
            .Returns(targetPath);
        _imageFileService.Setup(f => f.SaveImageWithMetadataAsync(targetPath, It.IsAny<byte[]>(), parameters))
            .ThrowsAsync(new IOException("simulated early failure"));

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        outcome.Kind.Should().Be(JobOutcomeKind.Failed);
        outcome.Message.Should().Contain("simulated early failure");
        File.Exists(targetPath).Should().BeFalse();
    }
}
