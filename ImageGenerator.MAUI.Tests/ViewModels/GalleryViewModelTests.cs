using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

public class GalleryViewModelTests
{
    private readonly Mock<IGalleryService> _galleryService = new();
    private readonly Mock<IFileLauncher> _fileLauncher = new();

    private GalleryViewModel CreateSut() => new(_galleryService.Object, _fileLauncher.Object);

    private static GalleryItem MakeItem(string fileName, long size = 1234L, DateTime? createdAt = null) => new(
        FilePath: Path.Combine("C:", "fake", fileName),
        FileName: fileName,
        CreatedAt: createdAt ?? new DateTime(2026, 1, 1, 12, 0, 0),
        FileSize: size,
        Metadata: null);

    private static IAsyncEnumerable<GalleryItem> AsAsync(IEnumerable<GalleryItem> items) => ToAsync(items);

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task RefreshAsync_PopulatesItems_FromGalleryService()
    {
        var items = new[] { MakeItem("c.png"), MakeItem("b.png"), MakeItem("a.png") };
        _galleryService
            .Setup(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsync(items));

        var sut = CreateSut();
        await ((IAsyncRelayCommand)sut.RefreshCommand).ExecuteAsync(null);

        sut.Items.Should().HaveCount(3);
        sut.Items.Select(i => i.FileName).Should().ContainInOrder("c.png", "b.png", "a.png");
        sut.IsBusy.Should().BeFalse();
        sut.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_OnEmptyResult_LeavesItemsEmpty_AndIsEmptyTrue()
    {
        _galleryService
            .Setup(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsync(Array.Empty<GalleryItem>()));

        var sut = CreateSut();
        await ((IAsyncRelayCommand)sut.RefreshCommand).ExecuteAsync(null);

        sut.Items.Should().BeEmpty();
        sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAsync_RunTwice_ReplacesItems_DoesNotAccumulate()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0);
        var first = new[] { MakeItem("a.png", createdAt: t) };
        var second = new[]
        {
            MakeItem("b.png", createdAt: t.AddSeconds(1)),
            MakeItem("c.png", createdAt: t.AddSeconds(2)),
        };

        _galleryService
            .SetupSequence(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsync(first))
            .Returns(AsAsync(second));

        var sut = CreateSut();
        await ((IAsyncRelayCommand)sut.RefreshCommand).ExecuteAsync(null);
        await ((IAsyncRelayCommand)sut.RefreshCommand).ExecuteAsync(null);

        sut.Items.Should().HaveCount(2);
        // Default sort is Newest first (CreationTime desc) — c is newer than b.
        sut.Items.Select(i => i.FileName).Should().ContainInOrder("c.png", "b.png");
    }

    [Fact]
    public void OpenInViewerCommand_CallsLauncherWithItemPath()
    {
        var sut = CreateSut();
        var item = MakeItem("hello.png");

        sut.OpenInViewerCommand.Execute(item);

        _fileLauncher.Verify(l => l.Launch(item.FilePath), Times.Once);
    }

    [Fact]
    public void OpenInViewerCommand_NullItem_DoesNothing()
    {
        var sut = CreateSut();

        sut.OpenInViewerCommand.Execute(null);

        _fileLauncher.Verify(l => l.Launch(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void OpenInViewerCommand_LauncherThrows_DoesNotPropagate()
    {
        _fileLauncher.Setup(l => l.Launch(It.IsAny<string>())).Throws<InvalidOperationException>();
        var sut = CreateSut();

        var act = () => sut.OpenInViewerCommand.Execute(MakeItem("x.png"));

        act.Should().NotThrow();
    }

    [Fact]
    public async Task IsEmpty_StartsTrue_AndFlipsAfterRefreshPopulatesItems()
    {
        var items = new[] { MakeItem("only.png") };
        _galleryService
            .Setup(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsync(items));

        var sut = CreateSut();
        sut.IsEmpty.Should().BeTrue("nothing has been loaded yet");

        await ((IAsyncRelayCommand)sut.RefreshCommand).ExecuteAsync(null);

        sut.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task SortMode_NewestFirst_OrdersByCreatedAtDescending()
    {
        // Filename intentionally inverse-aligned with date so a filename-based sort would
        // produce a different order — confirms the sort is genuinely date-driven.
        var t = new DateTime(2026, 1, 1, 12, 0, 0);
        var items = new[]
        {
            MakeItem("c.png", createdAt: t),                 // oldest
            MakeItem("a.png", createdAt: t.AddSeconds(2)),   // newest
            MakeItem("b.png", createdAt: t.AddSeconds(1)),   // middle
        };
        _galleryService
            .Setup(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsync(items));

        var sut = CreateSut();
        await ((IAsyncRelayCommand)sut.RefreshCommand).ExecuteAsync(null);

        sut.Items.Select(i => i.FileName).Should().ContainInOrder("a.png", "b.png", "c.png");
    }

    [Fact]
    public async Task SortMode_OldestFirst_OrdersByCreatedAtAscending()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0);
        var items = new[]
        {
            MakeItem("a.png", createdAt: t.AddSeconds(2)),   // newest
            MakeItem("c.png", createdAt: t),                 // oldest
            MakeItem("b.png", createdAt: t.AddSeconds(1)),   // middle
        };
        _galleryService
            .Setup(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsync(items));

        var sut = CreateSut();
        sut.SelectedSortMode = GalleryViewModel.SortOldestFirst;
        await ((IAsyncRelayCommand)sut.RefreshCommand).ExecuteAsync(null);

        sut.Items.Select(i => i.FileName).Should().ContainInOrder("c.png", "b.png", "a.png");
    }

    [Fact]
    public async Task SortMode_ChangedAfterRefresh_ResortsWithoutRefetching()
    {
        // Switching sort modes must not re-walk the directory; we hold an in-memory snapshot.
        var items = new[] { MakeItem("a.png", size: 100), MakeItem("b.png", size: 300), MakeItem("c.png", size: 200) };
        _galleryService
            .Setup(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsync(items));

        var sut = CreateSut();
        await ((IAsyncRelayCommand)sut.RefreshCommand).ExecuteAsync(null);

        sut.SelectedSortMode = GalleryViewModel.SortLargestFirst;

        sut.Items.Select(i => i.FileName).Should().ContainInOrder("b.png", "c.png", "a.png");
        _galleryService.Verify(s => s.EnumerateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SortMode_SmallestFirst_OrdersBySizeAscending()
    {
        var items = new[] { MakeItem("a.png", size: 300), MakeItem("b.png", size: 100), MakeItem("c.png", size: 200) };
        _galleryService
            .Setup(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsync(items));

        var sut = CreateSut();
        sut.SelectedSortMode = GalleryViewModel.SortSmallestFirst;
        await ((IAsyncRelayCommand)sut.RefreshCommand).ExecuteAsync(null);

        sut.Items.Select(i => i.FileName).Should().ContainInOrder("b.png", "c.png", "a.png");
    }

    [Fact]
    public void SortModes_ContainSixOptions()
    {
        var sut = CreateSut();
        sut.SortModes.Should().HaveCount(6);
        sut.SortModes.Should().Contain(GalleryViewModel.SortNewestFirst);
        sut.SortModes.Should().Contain(GalleryViewModel.SortOldestFirst);
    }

    [Fact]
    public void Dispose_DoesNotThrow_OnFreshlyConstructedViewModel()
    {
        var sut = CreateSut();

        var act = () => sut.Dispose();

        act.Should().NotThrow();
    }
}
