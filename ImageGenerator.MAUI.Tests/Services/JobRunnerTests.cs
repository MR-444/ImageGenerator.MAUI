using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
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
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
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
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
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
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new GeneratedImage { ImageData = Array.Empty<byte>(), Message = "empty" });

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        outcome.Kind.Should().Be(JobOutcomeKind.Failed);
        outcome.SavedPath.Should().BeNull();
        outcome.Message.Should().Be("empty");
    }

    [Fact]
    public async Task RunAsync_ForwardsProgressSinkToService()
    {
        var parameters = SampleParameters();
        var progress = new Progress<JobProgress>();
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>(), progress))
            .ReturnsAsync(new GeneratedImage { ImageData = null, Message = "x" })
            .Verifiable();

        await _sut.RunAsync(parameters, CancellationToken.None, progress);

        _imageService.Verify(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>(), progress), Times.Once,
            "the job card's progress sink must reach the generation service untouched");
    }

    [Fact]
    public async Task RunAsync_ServiceThrowsOperationCanceled_Propagates()
    {
        // The VM's RunJobAsync distinguishes OperationCanceledException from other failures, so
        // JobRunner must not catch it — let it bubble.
        var parameters = SampleParameters();
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
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
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
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

    // ---- Chained upscale ---------------------------------------------------------------------

    private static readonly byte[] RenderBytes = [1, 2, 3];
    private static readonly byte[] UpscaledBytes = [9, 9, 9, 9];

    private static ImageGenerationParameters ChainParameters() => new()
    {
        Model = "comfyui/Render-Flow",
        Prompt = "a cat",
        Seed = 42,
        UpscaleAfterRender = true,
        UpscaleWorkflow = "Upscale-Sample",
        UseJsonPrompt = true
    };

    /// <summary>Render succeeds and saves to <paramref name="renderPath"/>; captures the second pass's params.</summary>
    private void SetupChainRender(ImageGenerationParameters parameters, string renderPath)
    {
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new GeneratedImage { ImageData = RenderBytes, Message = "ok" });
        _imageFileService.Setup(f => f.GetUniqueSavePath(It.IsAny<string>(), parameters))
            .Returns(renderPath);
        _imageFileService.Setup(f => f.SaveImageWithMetadataAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<ImageGenerationParameters>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task RunAsync_ChainedUpscale_RunsSecondPassAndSavesWithSuffix()
    {
        var parameters = ChainParameters();
        var renderPath = PathInTempDir();
        SetupChainRender(parameters, renderPath);
        ImageGenerationParameters? upscalePass = null;
        _imageService.Setup(s => s.GenerateImageAsync(
                It.Is<ImageGenerationParameters>(p => p != parameters), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .Callback<ImageGenerationParameters, CancellationToken, IProgress<JobProgress>?>((p, _, _) => upscalePass = p)
            .ReturnsAsync(new GeneratedImage { ImageData = UpscaledBytes, Message = "ok" });

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        var expectedUpscaledPath = PathInTempDir("out_upscaled.png");
        outcome.Kind.Should().Be(JobOutcomeKind.Saved);
        outcome.SavedPath.Should().Be(expectedUpscaledPath, "downstream side effects should act on the upscaled image");
        outcome.Message.Should().Contain(renderPath).And.Contain(expectedUpscaledPath);

        upscalePass.Should().NotBeNull();
        upscalePass!.Model.Should().Be("comfyui/Upscale-Sample");
        upscalePass.ImagePrompts.Should().Equal(Convert.ToBase64String(RenderBytes));
        upscalePass.UseJsonPrompt.Should().BeFalse("upscale graphs take plain tile conditioning");
        upscalePass.UpscaleAfterRender.Should().BeFalse("an upscale must never chain another upscale");

        _imageFileService.Verify(
            f => f.SaveImageWithMetadataAsync(expectedUpscaledPath, UpscaledBytes, upscalePass), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ChainedUpscale_SuffixCollision_AppendsCounter()
    {
        var parameters = ChainParameters();
        var renderPath = PathInTempDir();
        await File.WriteAllBytesAsync(PathInTempDir("out_upscaled.png"), [0]);
        SetupChainRender(parameters, renderPath);
        _imageService.Setup(s => s.GenerateImageAsync(
                It.Is<ImageGenerationParameters>(p => p != parameters), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new GeneratedImage { ImageData = UpscaledBytes, Message = "ok" });

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        outcome.SavedPath.Should().Be(PathInTempDir("out_upscaled_1.png"));
    }

    [Theory]
    [InlineData("black-forest-labs/flux-dev", "Upscale-Sample")] // chain is ComfyUI-only
    [InlineData("comfyui/Upscale-Sample", "Upscale-Sample")]     // never re-upscale an upscale
    [InlineData("comfyui/Render-Flow", "")]                      // no designated workflow
    public async Task RunAsync_ChainedUpscale_SkippedWhenNotEligible(string model, string upscaleWorkflow)
    {
        var parameters = ChainParameters();
        parameters.Model = model;
        parameters.UpscaleWorkflow = upscaleWorkflow;
        var renderPath = PathInTempDir();
        SetupChainRender(parameters, renderPath);

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        outcome.SavedPath.Should().Be(renderPath);
        _imageService.Verify(
            s => s.GenerateImageAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()),
            Times.Once, "no second pass may run for an ineligible job");
    }

    [Fact]
    public async Task RunAsync_ChainedUpscale_UpscaleFails_KeepsRenderAsPartialSuccess()
    {
        var parameters = ChainParameters();
        var renderPath = PathInTempDir();
        SetupChainRender(parameters, renderPath);
        _imageService.Setup(s => s.GenerateImageAsync(
                It.Is<ImageGenerationParameters>(p => p != parameters), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new GeneratedImage { ImageData = null, Message = "server exploded" });

        var outcome = await _sut.RunAsync(parameters, CancellationToken.None);

        outcome.Kind.Should().Be(JobOutcomeKind.Saved, "the render is already on disk");
        outcome.SavedPath.Should().Be(renderPath);
        outcome.Message.Should().Contain("server exploded");
    }

    [Fact]
    public async Task RunAsync_ChainedUpscale_RelabelsRenderingProgressToUpscaling()
    {
        var parameters = ChainParameters();
        var renderPath = PathInTempDir();
        SetupChainRender(parameters, renderPath);
        _imageService.Setup(s => s.GenerateImageAsync(
                It.Is<ImageGenerationParameters>(p => p != parameters), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .Callback<ImageGenerationParameters, CancellationToken, IProgress<JobProgress>?>(
                (_, _, progress) => progress!.Report(new JobProgress("Rendering… 3/10", 0.3)))
            .ReturnsAsync(new GeneratedImage { ImageData = UpscaledBytes, Message = "ok" });
        var reports = new List<JobProgress>();
        var sink = new InlineProgress(reports.Add);

        await _sut.RunAsync(parameters, CancellationToken.None, sink);

        reports.Should().Contain(p => p.Message == "Upscaling… 3/10" && p.Percent == 0.3);
    }

    /// <summary>Progress&lt;T&gt; posts via SynchronizationContext (racy in tests); this reports inline.</summary>
    private sealed class InlineProgress(Action<JobProgress> onReport) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => onReport(value);
    }

    [Fact]
    public async Task RunAsync_SaveThrowsWithNoPartialFile_ReturnsFailedWithoutThrowing()
    {
        // If the save throws before any bytes hit disk (e.g. path-too-long check), the cleanup
        // branch must still execute cleanly (File.Exists returns false; no delete attempt).
        var parameters = SampleParameters();
        var targetPath = PathInTempDir("never-written.png");
        _imageService.Setup(s => s.GenerateImageAsync(parameters, It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
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
