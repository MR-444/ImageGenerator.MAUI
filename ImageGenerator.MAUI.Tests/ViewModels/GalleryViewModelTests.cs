using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

public class GalleryViewModelTests
{
    private readonly Mock<IGalleryService> _galleryService = new();
    private readonly Mock<IFileLauncher> _fileLauncher = new();
    private readonly Mock<ICivitaiPostingService> _civitaiPostingService = new();
    private readonly Mock<IUiStateStore> _uiStateStore = new();

    private GalleryViewModel CreateSut() => new(
        _galleryService.Object, _fileLauncher.Object, _civitaiPostingService.Object,
        _uiStateStore.Object, NullLogger<GalleryViewModel>.Instance);

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

    // ---- CivitAI batch posting ----

    private sealed class CapturedPost
    {
        public List<CivitaiImagePost> Images { get; } = [];
        public bool Publish { get; set; }
        public int? ModelVersionId { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private CapturedPost SetupCapturingPostingService(CivitaiPostResult? result = null)
    {
        // A class, not a tuple: the Callback runs AFTER this method returns, so value-type
        // fields on a returned struct would be snapshotted empty. A reference type is mutated
        // in place and the test sees the captured values.
        var captured = new CapturedPost();
        _civitaiPostingService
            .Setup(s => s.PostImagesAsync(
                It.IsAny<IReadOnlyList<CivitaiImagePost>>(), It.IsAny<string>(),
                It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<CivitaiImagePost>, string, int?, bool, CancellationToken>(
                (imgs, title, mv, pub, _) =>
                {
                    captured.Images.AddRange(imgs);
                    captured.Publish = pub;
                    captured.ModelVersionId = mv;
                    captured.Title = title;
                })
            .ReturnsAsync(result ?? new CivitaiPostResult(true, 123, "https://civitai.com/posts/123", "Created CivitAI draft."));
        return captured;
    }

    [Fact]
    public void HasSelection_TracksSelectedItems()
    {
        var sut = CreateSut();
        sut.HasSelection.Should().BeFalse();

        sut.SelectedItems.Add(MakeItem("a.png"));
        sut.HasSelection.Should().BeTrue();

        sut.SelectedItems.Clear();
        sut.HasSelection.Should().BeFalse();
    }

    [Fact]
    public void PostSelectedAsOnePostCommand_NoSelection_CannotExecute()
    {
        var sut = CreateSut();

        ((IAsyncRelayCommand)sut.PostSelectedAsOnePostCommand).CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task PostSelectedAsOnePost_PostsAllSelected_AsOneDraft_WithMeta()
    {
        _galleryService
            .Setup(s => s.ReadMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["Prompt"] = "a fox", ["Seed"] = "42" });
        var captured = SetupCapturingPostingService();

        var sut = CreateSut();
        sut.SelectedItems.Add(MakeItem("a.png"));
        sut.SelectedItems.Add(MakeItem("b.png"));

        await ((IAsyncRelayCommand)sut.PostSelectedAsOnePostCommand).ExecuteAsync(null);

        // Exactly one service call (one post), with both images and publish:false (draft).
        _civitaiPostingService.Verify(s => s.PostImagesAsync(
            It.IsAny<IReadOnlyList<CivitaiImagePost>>(), It.IsAny<string>(),
            It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        captured.Images.Should().HaveCount(2);
        captured.Images.Select(i => Path.GetFileName(i.FilePath)).Should().ContainInOrder("a.png", "b.png");
        captured.Publish.Should().BeFalse("the Gallery batch always drafts for review");
        captured.Images[0].Meta.Should().NotBeNull();
        captured.Images[0].Meta!["prompt"].Should().Be("a fox");
        captured.Title.Should().Be("a fox", "title derives from the first image's prompt");

        sut.CivitaiStatusMessage.Should().Be("Created CivitAI draft.");
        sut.LastPostUrl.Should().Be("https://civitai.com/posts/123");
        sut.HasLastPost.Should().BeTrue();
    }

    [Fact]
    public async Task PostSelectedAsOnePost_IncludeMetaOff_SendsNullMeta()
    {
        _galleryService
            .Setup(s => s.ReadMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["Prompt"] = "a fox" });
        var captured = SetupCapturingPostingService();

        var sut = CreateSut();
        sut.CivitaiIncludeMeta = false;
        sut.SelectedItems.Add(MakeItem("a.png"));

        await ((IAsyncRelayCommand)sut.PostSelectedAsOnePostCommand).ExecuteAsync(null);

        captured.Images.Should().ContainSingle();
        captured.Images[0].Meta.Should().BeNull("generation data is opt-out per batch");
    }

    [Fact]
    public async Task PostSelectedAsOnePost_ParsesModelVersionIdFromRef()
    {
        _galleryService
            .Setup(s => s.ReadMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["Prompt"] = "p" });
        var captured = SetupCapturingPostingService();

        var sut = CreateSut();
        sut.CivitaiModelRef = "https://civitai.com/models/123?modelVersionId=3005491";
        sut.SelectedItems.Add(MakeItem("a.png"));

        await ((IAsyncRelayCommand)sut.PostSelectedAsOnePostCommand).ExecuteAsync(null);

        captured.ModelVersionId.Should().Be(3005491);
    }

    [Fact]
    public void CivitaiModelRef_Changed_PersistsToUiStateStore()
    {
        var sut = CreateSut();

        sut.CivitaiModelRef = "3005491";

        _uiStateStore.Verify(u => u.PersistCivitaiModelRef("3005491"), Times.Once);
    }

    [Fact]
    public async Task OnAppearing_RestoresSavedModelRef()
    {
        _uiStateStore.Setup(u => u.LoadCivitaiModelRef()).Returns("999");
        _galleryService
            .Setup(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsync(Array.Empty<GalleryItem>()));

        var sut = CreateSut();
        await sut.OnAppearingAsync();
        sut.Dispose();

        sut.CivitaiModelRef.Should().Be("999");
    }

    [Fact]
    public async Task OpenLastPostCommand_LaunchesUrl()
    {
        var sut = CreateSut();
        // Drive LastPostUrl via a completed post so the command has something to open.
        SetupCapturingPostingService(new CivitaiPostResult(true, 1, "https://civitai.com/posts/1", "ok"));
        _galleryService
            .Setup(s => s.ReadMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string>?)null);
        sut.SelectedItems.Add(MakeItem("a.png"));
        await ((IAsyncRelayCommand)sut.PostSelectedAsOnePostCommand).ExecuteAsync(null);

        sut.OpenLastPostCommand.Execute(null);

        _fileLauncher.Verify(l => l.Launch("https://civitai.com/posts/1"), Times.Once);
    }
}
