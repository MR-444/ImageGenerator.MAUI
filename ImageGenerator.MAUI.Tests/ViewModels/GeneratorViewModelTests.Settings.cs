using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

// Settings-state tests, lifted verbatim out of GeneratorViewModelTests.cs. Same fixture on
// purpose - it is one partial class, so [Collection("OutputPathsState")] and the ctor still
// govern these tests, which still build a GeneratorViewModel because the settings state has
// no owner of its own yet. Splitting here keeps Phase 2 to a file move instead of a
// reach-through a 3,200-line fixture.
public partial class GeneratorViewModelTests
{
    [Fact]
    public async Task RefreshOpenRouterVisionModels_FreeOnly_ClearsPaidSelection()
    {
        _viewModel.OpenRouterVisionFreeOnly = true;
        _viewModel.OpenRouterVisionModel = "paid/vision";
        _mockOpenRouterModelCatalog
            .Setup(c => c.ListVisionModelsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OpenRouterModelInfo("free/vision:free", "Free Vision", true)]);

        await _viewModel.RefreshOpenRouterVisionModelsCommand.ExecuteAsync(null);

        _viewModel.OpenRouterVisionModels.Should().Equal("free/vision:free");
        _viewModel.OpenRouterVisionModel.Should().BeEmpty();
    }

    [Fact]
    public void LoadSavedUiState_FreeOnly_DoesNotRestoreSavedOpenRouterModel()
    {
        _mockUiStateStore.Setup(s => s.LoadOpenRouterVisionFreeOnly()).Returns(true);
        _mockUiStateStore.Setup(s => s.LoadOpenRouterVisionModel()).Returns("paid/vision");

        _viewModel.LoadSavedUiState();

        _viewModel.OpenRouterVisionFreeOnly.Should().BeTrue();
        _viewModel.OpenRouterVisionModel.Should().BeEmpty();
        _viewModel.OpenRouterVisionModels.Should().BeEmpty();
    }

    [Fact]
    public void LoadSavedUiState_FreeOnly_RestoresSavedFreeOpenRouterModel()
    {
        _mockUiStateStore.Setup(s => s.LoadOpenRouterVisionFreeOnly()).Returns(true);
        _mockUiStateStore.Setup(s => s.LoadOpenRouterVisionModel()).Returns("free/vision:free");

        _viewModel.LoadSavedUiState();

        _viewModel.OpenRouterVisionModel.Should().Be("free/vision:free");
        _viewModel.OpenRouterVisionModels.Should().ContainSingle().Which.Should().Be("free/vision:free");
    }

    [Fact]
    public void UseSageAttention_WhenChanged_PersistsHostSetting()
    {
        _viewModel.UseSageAttention = true;

        _mockUiStateStore.Verify(s => s.PersistUseSageAttention(true), Times.Once);
    }

    [Fact]
    public void LoadSavedUiState_RestoresUseSageAttention()
    {
        _mockUiStateStore.Setup(s => s.LoadUseSageAttention()).Returns(true);

        _viewModel.LoadSavedUiState();

        _viewModel.UseSageAttention.Should().BeTrue();
        _mockUiStateStore.Verify(s => s.LoadUseSageAttention(), Times.Once);
    }

    // --- Configurable output folder ---

    [Fact]
    public async Task BrowseOutputFolder_WhenUserPicksFolder_SetsPersistsAndAppliesOverride()
    {
        const string picked = @"D:\my images\out";
        _mockFolderPicker.Setup(p => p.PickFolderAsync(It.IsAny<string?>())).ReturnsAsync(picked);
        try
        {
            await _viewModel.BrowseOutputFolderCommand.ExecuteAsync(null);

            _viewModel.OutputFolder.Should().Be(picked);
            _mockUiStateStore.Verify(s => s.PersistOutputFolder(picked), Times.Once);
            // The picked folder is the root; images land in its "pictures" subfolder.
            OutputPaths.RootDirectory.Should().Be(picked);
            OutputPaths.GeneratedImagesDirectory.Should().Be(Path.Combine(picked, "pictures"));
        }
        finally
        {
            // Process-global static state — never leak the override into sibling tests.
            OutputPaths.SetRootOverride(null);
        }
    }

    [Fact]
    public async Task BrowseOutputFolder_WhenCancelled_LeavesValueUnchanged()
    {
        var original = _viewModel.OutputFolder;
        _mockFolderPicker.Setup(p => p.PickFolderAsync(It.IsAny<string?>())).ReturnsAsync((string?)null);

        await _viewModel.BrowseOutputFolderCommand.ExecuteAsync(null);

        _viewModel.OutputFolder.Should().Be(original);
        _mockUiStateStore.Verify(s => s.PersistOutputFolder(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void OutputFolder_WhenSet_AppliesOverrideToOutputPaths()
    {
        try
        {
            _viewModel.OutputFolder = @"E:\elsewhere";

            OutputPaths.GeneratedImagesDirectory.Should().Be(Path.Combine(@"E:\elsewhere", "pictures"));
            // Every data folder follows the configured root now, including comfy-workflows.
            OutputPaths.JsonPromptsDirectory.Should().StartWith(@"E:\elsewhere");
            OutputPaths.ComfyWorkflowsDirectory.Should().Be(
                Path.Combine(@"E:\elsewhere", "comfy-workflows"));
        }
        finally
        {
            OutputPaths.SetRootOverride(null);
        }
    }

    // ---- Ollama model picker: refresh must not destroy the saved selection --------------

    // The reported bug. A MAUI Picker bound to OllamaModel pushes null back the moment its
    // ItemsSource is cleared, which every refresh does; unsuppressed that persisted an empty
    // string and the next launch had nothing to restore.
    [Fact]
    public async Task RefreshOllamaModels_KeepsTheSelection_WhenTheModelIsStillInstalled()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("llama3.1"), Model("gemma3:27b", vision: true)]);
        vm.OllamaModel = "llama3.1";

        await vm.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.OllamaModel.Should().Be("llama3.1", "the pick is still installed");
        vm.OllamaModels.Should().BeEquivalentTo(["llama3.1", "gemma3:27b"]);
        store.Verify(s => s.PersistOllamaModel(string.Empty), Times.Never,
            "a Picker's null push during the rebuild must never reach Preferences");
    }

    [Fact]
    public async Task RefreshOllamaModels_DropsTheSelection_WhenTheModelIsGone()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("gemma3:27b", vision: true)]);
        vm.OllamaModel = "deleted-on-server";

        await vm.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.OllamaModel.Should().BeEmpty("a ghost entry only fails later as an opaque 404");
        vm.OllamaModels.Should().NotContain("deleted-on-server");
        vm.StatusKind.Should().Be(StatusKind.Warning);
        vm.StatusMessage.Should().Contain("deleted-on-server").And.Contain("no longer installed");
        store.Verify(s => s.PersistOllamaModel(string.Empty), Times.Once, "the drop is deliberate and must stick");
    }

    // Still installed, but no longer advertises vision — dropped from the vision picker only, and
    // still worth naming, otherwise the selection silently empties with no explanation.
    [Fact]
    public async Task RefreshOllamaModels_DropsTheVisionPick_WhenTheModelStopsAdvertisingVision()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("llama3.1")]);
        vm.OllamaModel = "llama3.1";
        vm.OllamaVisionModel = "llama3.1";

        await vm.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.OllamaModel.Should().Be("llama3.1", "it is still installed for text");
        vm.OllamaVisionModel.Should().BeEmpty("it can no longer serve a vision request");
        vm.StatusKind.Should().Be(StatusKind.Warning);
        vm.StatusMessage.Should().Contain("llama3.1");
        store.Verify(s => s.PersistOllamaVisionModel(string.Empty), Times.Once);
    }

    [Fact]
    public async Task RefreshOllamaModels_WhenTheServerIsDown_LeavesTheSelectionAndListIntact()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        vm.OllamaModel = "llama3.1";
        vm.OllamaModels.Add("llama3.1");

        await vm.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.OllamaModel.Should().Be("llama3.1", "an unreachable server proves nothing about the model");
        vm.OllamaModels.Should().ContainSingle().Which.Should().Be("llama3.1");
        vm.StatusKind.Should().Be(StatusKind.Error);
        store.Verify(s => s.PersistOllamaModel(string.Empty), Times.Never);
    }

    // A reachable server answering with zero models looks identical to a misconfigured URL or a
    // proxy answering for one, so it must not be treated as proof the pick is gone.
    [Fact]
    public async Task RefreshOllamaModels_WhenTheServerListsNothing_LeavesTheSelectionIntact()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        vm.OllamaModel = "llama3.1";

        await vm.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.OllamaModel.Should().Be("llama3.1");
        vm.StatusKind.Should().Be(StatusKind.Warning);
        store.Verify(s => s.PersistOllamaModel(string.Empty), Times.Never);
    }

    [Fact]
    public async Task RefreshOllamaModels_CachesTheListForTheNextLaunch()
    {
        var vm = OllamaVm(out var catalog, out var store);
        vm.OllamaBaseUrl = "http://fireengine:11434";
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("llama3.1"), Model("gemma3:27b", vision: true)]);

        await vm.RefreshOllamaModelsCommand.ExecuteAsync(null);

        store.Verify(s => s.PersistOllamaModels(
            "http://fireengine:11434",
            It.Is<IReadOnlyList<string>>(m => m.SequenceEqual(new[] { "llama3.1", "gemma3:27b" })),
            It.Is<IReadOnlyList<string>>(v => v.SequenceEqual(new[] { "gemma3:27b" }))), Times.Once);
    }

    [Fact]
    public async Task EnsureOllamaModelsLoaded_FetchesOncePerSession_AndStaysSilent()
    {
        var vm = OllamaVm(out var catalog, out _);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("llama3.1")]);

        await vm.EnsureOllamaModelsLoadedAsync();
        await vm.EnsureOllamaModelsLoadedAsync();

        catalog.Verify(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "every page appearance must not re-probe the server");
        vm.OllamaModels.Should().Contain("llama3.1");
        vm.StatusMessage.Should().BeNullOrEmpty("the automatic load is not a user action worth narrating");
    }

    [Fact]
    public void LoadSavedUiState_SeedsThePickersFromTheCachedList()
    {
        _mockUiStateStore.Setup(s => s.LoadOllamaModels(It.IsAny<string>()))
            .Returns((string[])["llama3.1", "gemma3:27b"]);
        _mockUiStateStore.Setup(s => s.LoadOllamaVisionModels(It.IsAny<string>()))
            .Returns((string[])["gemma3:27b"]);
        _mockUiStateStore.Setup(s => s.LoadOllamaModel()).Returns("llama3.1");

        _viewModel.LoadSavedUiState();

        _viewModel.OllamaModels.Should().BeEquivalentTo((string[])["llama3.1", "gemma3:27b"],
            "the picker is usable at launch, and for the whole session if the server is down");
        _viewModel.OllamaVisionModels.Should().BeEquivalentTo(["gemma3:27b"]);
        _viewModel.OllamaModel.Should().Be("llama3.1");
    }

    [Fact]
    public void OllamaModel_HasNoImplicitDefault()
    {
        _viewModel.OllamaModel.Should().BeEmpty("which models exist is per-server; guessing one only 404s");
        _viewModel.OllamaVisionModel.Should().BeEmpty();
    }

    private static OllamaModelInfo Model(string name, bool vision = false) =>
        new(name, vision ? ["completion", "vision"] : ["completion"]);

    // A VM wired to an Ollama catalog — the base _viewModel has none, so its refresh no-ops.
    private GeneratorViewModel OllamaVm(out Mock<IOllamaModelCatalog> catalog, out Mock<IUiStateStore> store)
    {
        catalog = new Mock<IOllamaModelCatalog>();
        store = new Mock<IUiStateStore>();
        return new GeneratorViewModel(
            _mockJobRunner.Object, _mockTokenStore.Object, _mockPollinationsTokenStore.Object,
            _mockComfyUiAuthStore.Object, _mockCivitaiTokenStore.Object, _mockAnthropicTokenStore.Object,
            _mockCivitaiPostingService.Object, store.Object, _mockCatalogCoordinator.Object,
            ModelDescriptorRegistry.Default(), _mockPromptBatchParser.Object, _mockCheckpointService.Object,
            _mockGalleryService.Object, _mockFolderPicker.Object, NullLogger<GeneratorViewModel>.Instance,
            ollamaModelCatalog: catalog.Object);
    }
}
