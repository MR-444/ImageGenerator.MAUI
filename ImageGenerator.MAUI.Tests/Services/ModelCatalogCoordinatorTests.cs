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
    private readonly ModelCatalogCoordinator _sut;

    public ModelCatalogCoordinatorTests()
    {
        _sut = new ModelCatalogCoordinator(_catalogService.Object, ModelDescriptorRegistry.Default());
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
            .ReturnsAsync(Array.Empty<ModelOption>());

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
            new("flux-2-pro", "black-forest-labs/flux-2-pro", ProviderConstants.BlackForestLabs)
        };
        _catalogService.Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleCache);

        var merged = await _sut.LoadCachedAsync();

        merged.Should().NotBeNull();
        merged!.Select(m => m.Value).Should()
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
            .ReturnsAsync(Array.Empty<ModelOption>());

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
            new("flux-2", "black-forest-labs/flux-2", ProviderConstants.BlackForestLabs)
        };
        _catalogService.Setup(x => x.FetchAsync("token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetched);

        var merged = await _sut.RefreshAsync("token");

        merged.Should().NotBeNull();
        merged!.Select(m => m.Value).Should()
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
}
