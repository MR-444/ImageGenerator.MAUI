using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

public class GalleryItemDetailViewModelTests
{
    private readonly Mock<IGalleryService> _galleryService = new();
    private readonly Mock<IFileLauncher> _fileLauncher = new();
    private readonly Mock<IClipboardService> _clipboard = new();

    private GalleryItemDetailViewModel CreateSut() =>
        new(_galleryService.Object, _fileLauncher.Object, _clipboard.Object, NullLogger<GalleryItemDetailViewModel>.Instance);

    [Fact]
    public async Task LoadAsync_PopulatesFileNameAndMetadataText()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\fake\20260101_120000_test_42.png";
        var meta = new Dictionary<string, string>
        {
            ["Prompt"] = "a cat",
            ["Seed"] = "42",
            ["ModelName"] = "test/model",
        };
        _galleryService
            .Setup(s => s.ReadMetadataAsync(sut.FilePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(meta);

        await sut.LoadAsync();

        sut.FileName.Should().Be("20260101_120000_test_42.png");
        sut.MetadataText.Should().NotBeNull();
        // Preferred ordering puts Prompt, ModelName, Seed at the top.
        var lines = sut.MetadataText!.Split(Environment.NewLine);
        lines[0].Should().Be("Prompt: a cat");
        lines[1].Should().Be("ModelName: test/model");
        lines[2].Should().Be("Seed: 42");
        sut.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_NoMetadata_SetsFriendlyText()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\fake\nada.png";
        _galleryService
            .Setup(s => s.ReadMetadataAsync(sut.FilePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string>?)null);

        await sut.LoadAsync();

        sut.MetadataText.Should().Contain("No embedded metadata");
    }

    [Fact]
    public async Task LoadAsync_EmptyFilePath_ReturnsEarly()
    {
        var sut = CreateSut();
        sut.FilePath = string.Empty;

        await sut.LoadAsync();

        _galleryService.Verify(s => s.ReadMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopyMetadataCommand_WritesMetadataTextToClipboard()
    {
        var sut = CreateSut();
        sut.MetadataText = "Prompt: hello";

        await ((IAsyncRelayCommand)sut.CopyMetadataCommand).ExecuteAsync(null);

        _clipboard.Verify(c => c.SetTextAsync("Prompt: hello"), Times.Once);
    }

    [Fact]
    public async Task CopyMetadataCommand_EmptyText_DoesNothing()
    {
        var sut = CreateSut();
        sut.MetadataText = "";

        await ((IAsyncRelayCommand)sut.CopyMetadataCommand).ExecuteAsync(null);

        _clipboard.Verify(c => c.SetTextAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void OpenInViewerCommand_CallsLauncher()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\fake\image.png";

        sut.OpenInViewerCommand.Execute(null);

        _fileLauncher.Verify(l => l.Launch(@"C:\fake\image.png"), Times.Once);
    }

    [Fact]
    public void ShowInFolderCommand_CallsRevealInFolder()
    {
        var sut = CreateSut();
        sut.FilePath = @"C:\fake\image.png";

        sut.ShowInFolderCommand.Execute(null);

        _fileLauncher.Verify(l => l.RevealInFolder(@"C:\fake\image.png"), Times.Once);
    }

    [Fact]
    public void OpenInViewerCommand_LauncherThrows_DoesNotPropagate()
    {
        _fileLauncher.Setup(l => l.Launch(It.IsAny<string>())).Throws<InvalidOperationException>();
        var sut = CreateSut();
        sut.FilePath = @"C:\fake\image.png";

        var act = () => sut.OpenInViewerCommand.Execute(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void OpenInViewerCommand_EmptyFilePath_DoesNothing()
    {
        var sut = CreateSut();
        sut.FilePath = null;

        sut.OpenInViewerCommand.Execute(null);

        _fileLauncher.Verify(l => l.Launch(It.IsAny<string>()), Times.Never);
    }
}
