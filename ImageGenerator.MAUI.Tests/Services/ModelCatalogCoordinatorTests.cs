using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Shared.Constants;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services;

public class ModelCatalogCoordinatorTests
{
    private readonly Mock<IModelCatalogService> _catalogService = new();
    private readonly Mock<IPollinationsCatalogService> _pollinationsCatalogService = new();
    private readonly Mock<IComfyUiWorkflowCatalogService> _comfyCatalogService = new();
    private readonly ModelCatalogCoordinator _sut;

    public ModelCatalogCoordinatorTests()
    {
        // Default the Pollinations + ComfyUI catalogs to empty so the existing assertions
        // (which only know about the Replicate path) keep matching exactly. Tests that
        // exercise the merges can override these setups.
        _pollinationsCatalogService
            .Setup(x => x.FetchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _comfyCatalogService
            .Setup(x => x.FetchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _sut = new ModelCatalogCoordinator(
            _catalogService.Object,
            _pollinationsCatalogService.Object,
            _comfyCatalogService.Object,
            ModelDescriptorRegistry.Default());
    }

    [Fact]
    public async Task LoadCachedAsync_NullCache_ReturnsNull()
    {
        _catalogService.Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModelOption>?)null);

        var result = await _sut.LoadCachedAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadCachedAsync_EmptyCache_ReturnsNull()
    {
        _catalogService.Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.LoadCachedAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadCachedAsync_StaleCache_StillSurfacesNewlyAddedSeed()
    {
        // The cache was written before gpt-image-2 was added to the seed list. The merge
        // must surface gpt-image-2 anyway so the user doesn't have to delete the cache file
        // (or wait for Replicate to catch up) to ever see freshly-added entries.
        var staleCache = new List<ModelOption>
        {
            new("flux-2-pro", "black-forest-labs/flux-2-pro", ProviderConstants.Replicate)
        };
        _catalogService.Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleCache);

        var merged = await _sut.LoadCachedAsync();

        merged.Should().NotBeNull();
        merged.Select(m => m.Value).Should()
            .Contain("openai/gpt-image-2")
            .And.Contain("black-forest-labs/flux-2-pro");
    }

    [Fact]
    public async Task LoadCachedAsync_LiveOverridesSeedOnDuplicateValue()
    {
        // When live data and seed both contain the same Value, live wins (its Display/Provider
        // reflect what Replicate actually returned).
        var live = new List<ModelOption>
        {
            new("Live GPT 1.5 Display", ModelConstants.OpenAI.GptImage15OnReplicate, "Live Provider")
        };
        _catalogService.Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);

        var merged = await _sut.LoadCachedAsync();

        var entry = merged!.Single(m => m.Value == ModelConstants.OpenAI.GptImage15OnReplicate);
        entry.Display.Should().Be("Live GPT 1.5 Display");
        entry.Provider.Should().Be("Live Provider");
    }

    [Fact]
    public async Task RefreshAsync_EmptyResult_ReturnsNull_AndDoesNotSave()
    {
        _catalogService.Setup(x => x.FetchAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.RefreshAsync("token");

        result.Should().BeNull();
        _catalogService.Verify(
            x => x.SaveCachedAsync(It.IsAny<IReadOnlyList<ModelOption>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_FetchedNonEmpty_SavesRawList_AndReturnsMerged()
    {
        var fetched = new List<ModelOption>
        {
            new("flux-2", "black-forest-labs/flux-2", ProviderConstants.Replicate)
        };
        _catalogService.Setup(x => x.FetchAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetched);

        var merged = await _sut.RefreshAsync("token");

        merged.Should().NotBeNull();
        merged.Select(m => m.Value).Should()
            .Contain("black-forest-labs/flux-2")
            .And.Contain(ModelConstants.OpenAI.GptImage15OnReplicate);  // from seed merge

        // Save persists the RAW fetched list, not the merged one (so load-time merge can
        // surface freshly-added seeds even when this cache was written earlier).
        _catalogService.Verify(
            x => x.SaveCachedAsync(
                It.Is<IReadOnlyList<ModelOption>>(l => l.SequenceEqual(fetched)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ModelOption ComfyEntry(string stem = "Ideogram workflow_MR") =>
        new($"{stem} (ComfyUI)", ModelConstants.ComfyUi.PrefixSlash + stem, ProviderConstants.ComfyUi);

    [Fact]
    public async Task LoadCachedAsync_NoCacheButWorkflowsExist_SurfacesThem()
    {
        // First launch: nothing was ever fetched/cached, but the user already exported
        // workflows — they (plus the seeds via merge) must appear without a Refresh.
        _catalogService.Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModelOption>?)null);
        _comfyCatalogService.Setup(x => x.FetchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComfyEntry()]);

        var merged = await _sut.LoadCachedAsync();

        merged.Should().NotBeNull();
        merged.Select(m => m.Value).Should().Contain("comfyui/Ideogram workflow_MR");
    }

    [Fact]
    public async Task LoadCachedAsync_MergesComfyEntriesWithTheCache()
    {
        _catalogService.Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ModelOption("flux-2", "black-forest-labs/flux-2", ProviderConstants.Replicate)]);
        _comfyCatalogService.Setup(x => x.FetchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComfyEntry()]);

        var merged = await _sut.LoadCachedAsync();

        merged!.Select(m => m.Value).Should()
            .Contain("black-forest-labs/flux-2")
            .And.Contain("comfyui/Ideogram workflow_MR");
    }

    [Fact]
    public async Task RefreshAsync_ComfyEntriesAreMergedButNeverCached()
    {
        var fetched = new List<ModelOption>
        {
            new("flux-2", "black-forest-labs/flux-2", ProviderConstants.Replicate)
        };
        _catalogService.Setup(x => x.FetchAsync("token", It.IsAny<CancellationToken>())).ReturnsAsync(fetched);
        _comfyCatalogService.Setup(x => x.FetchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComfyEntry()]);

        var merged = await _sut.RefreshAsync("token");

        merged!.Select(m => m.Value).Should().Contain("comfyui/Ideogram workflow_MR");
        // The disk cache stays provider-pure: the workflow folder is rescanned live every
        // load, so caching its entries would only let deleted workflows linger.
        _catalogService.Verify(
            x => x.SaveCachedAsync(It.Is<IReadOnlyList<ModelOption>>(
                l => l.SequenceEqual(fetched)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_OnlyComfyEntries_ReturnsThemWithoutSaving()
    {
        _catalogService.Setup(x => x.FetchAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _comfyCatalogService.Setup(x => x.FetchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComfyEntry()]);

        var merged = await _sut.RefreshAsync("token");

        merged.Should().NotBeNull();
        merged.Select(m => m.Value).Should().Contain("comfyui/Ideogram workflow_MR");
        _catalogService.Verify(
            x => x.SaveCachedAsync(It.IsAny<IReadOnlyList<ModelOption>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- entries this build has no field list for -------------------------------
    // Any model of an allow-listed owner appears in the catalog, so being listed has never meant the
    // app knows how to drive the model — and since this build refuses to call those models, the entry
    // has to say so. The refusal itself is tested in ReplicateImageGenerationServiceTests; these two
    // properties are about where the mark goes and, more importantly, where it must never go: disk.
    // See IModelDescriptorRegistry.IsSchemaKnown.

    private const string Marked = " · not supported";

    [Fact]
    public async Task RefreshAsync_MarksTheModelsThisBuildCannotDriveAndLeavesTheOthersAlone()
    {
        _catalogService.Setup(x => x.FetchAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ModelOption("Some New Model", "some-owner/some-new-model", ProviderConstants.Replicate),
                new ModelOption("FLUX.2 [klein] 4B", ModelConstants.Flux.Klein4b, ProviderConstants.Replicate)
            ]);
        // The two uniform providers are fetched separately, as they are in production.
        _pollinationsCatalogService.Setup(x => x.FetchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ModelOption("flux-schnell", "pollinations/flux-schnell", ProviderConstants.Pollinations)]);
        _comfyCatalogService.Setup(x => x.FetchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ComfyEntry()]);

        var merged = await _sut.RefreshAsync("token");

        merged!.Single(m => m.Value == "some-owner/some-new-model").Display.Should().Be("Some New Model" + Marked);
        merged!.Single(m => m.Value == ModelConstants.Flux.Klein4b).Display.Should().Be("FLUX.2 [klein] 4B");
        merged!.Single(m => m.Value == "pollinations/flux-schnell").Display.Should().Be("flux-schnell");
        // Not descriptor gaps: Pollinations speaks one uniform API for every id it lists, and a
        // ComfyUI entry is the user's own workflow. Marking them would teach the user to ignore the
        // mark, which is the opposite of what it is for.
        merged!.Single(m => m.Value == "comfyui/Ideogram workflow_MR").Display.Should().NotEndWith(Marked);
        // Seeds are the models this build ships descriptors for; the suffix must never reach one.
        merged!.Where(m => m.Value != "some-owner/some-new-model").Should().NotContain(m => m.Display.EndsWith(Marked, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_SavesTheCleanName_MarkingHappensOnTheWayOutOnly()
    {
        IReadOnlyList<ModelOption>? saved = null;
        _catalogService.Setup(x => x.SaveCachedAsync(It.IsAny<IReadOnlyList<ModelOption>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ModelOption>, CancellationToken>((list, _) => saved = list)
            .Returns(Task.CompletedTask);
        _catalogService.Setup(x => x.FetchAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ModelOption("Some New Model", "some-owner/some-new-model", ProviderConstants.Replicate)]);

        await _sut.RefreshAsync("token");

        // The cache stores what Replicate called the model. A mark that reached disk would outlive
        // its own truth: the build that finally adds a descriptor for this id would read a cached
        // "· not supported" straight back out and refuse a model it can now drive.
        saved!.Single(m => m.Value == "some-owner/some-new-model").Display.Should().Be("Some New Model");
    }
}
