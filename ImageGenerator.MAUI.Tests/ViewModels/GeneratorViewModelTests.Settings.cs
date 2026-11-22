using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Infrastructure.External.Ollama;
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
    public async Task RefreshOpenRouterVisionModels_FreeOnly_ListsFreeModelsAndTouchesNoPick()
    {
        _viewModel.Settings.OpenRouterVisionFreeOnly = true;
        _mockOpenRouterModelCatalog
            .Setup(c => c.ListVisionModelsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OpenRouterModelInfo("free/vision:free", "Free Vision", true)]);

        await _viewModel.Settings.RefreshOpenRouterVisionModelsCommand.ExecuteAsync(null);

        _viewModel.Settings.OpenRouterVisionModels.Should().Equal("free/vision:free");
        _mockUiStateStore.Verify(s => s.PersistLlmModel(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>(), It.IsAny<string?>()),
            Times.Never, "a price filter on a list is not proof a model is gone");
    }

    [Fact]
    public void LoadSavedUiState_RestoresTheFreeOnlySwitch()
    {
        _mockUiStateStore.Setup(s => s.LoadOpenRouterVisionFreeOnly()).Returns(false);

        _viewModel.LoadSavedUiState();

        _viewModel.Settings.OpenRouterVisionFreeOnly.Should().BeFalse();
    }

    [Fact]
    public void UseSageAttention_WhenChanged_PersistsHostSetting()
    {
        _viewModel.Settings.UseSageAttention = true;

        _mockUiStateStore.Verify(s => s.PersistUseSageAttention(true), Times.Once);
    }

    [Fact]
    public void LoadSavedUiState_RestoresUseSageAttention()
    {
        _mockUiStateStore.Setup(s => s.LoadUseSageAttention()).Returns(true);

        _viewModel.LoadSavedUiState();

        _viewModel.Settings.UseSageAttention.Should().BeTrue();
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
            await _viewModel.Settings.BrowseOutputFolderCommand.ExecuteAsync(null);

            _viewModel.Settings.OutputFolder.Should().Be(picked);
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
        var original = _viewModel.Settings.OutputFolder;
        _mockFolderPicker.Setup(p => p.PickFolderAsync(It.IsAny<string?>())).ReturnsAsync((string?)null);

        await _viewModel.Settings.BrowseOutputFolderCommand.ExecuteAsync(null);

        _viewModel.Settings.OutputFolder.Should().Be(original);
        _mockUiStateStore.Verify(s => s.PersistOutputFolder(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void OutputFolder_WhenSet_AppliesOverrideToOutputPaths()
    {
        try
        {
            _viewModel.Settings.OutputFolder = @"E:\elsewhere";

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

    // ---- Ollama refresh: rebuild the lists, keep every pick the server still has ---------------
    // The picks live in the store (each row writes its own); these pin what a refresh may do to them.

    [Fact]
    public async Task RefreshOllamaModels_KeepsAPick_WhenTheModelIsStillInstalled()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("llama3.1"), Model("gemma3:27b", vision: true)]);
        store.Setup(s => s.LoadLlmModel(ModelRole.Mutate, ModelProvider.Local)).Returns("llama3.1");
        store.Setup(s => s.LoadLlmModel(ModelRole.Observe, ModelProvider.Local)).Returns("gemma3:27b");

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.Settings.OllamaModels.Should().BeEquivalentTo(["llama3.1", "gemma3:27b"]);
        store.Verify(s => s.PersistLlmModel(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>(), It.IsAny<string?>()),
            Times.Never, "both picks are still installed");
    }

    // Still installed, but no longer advertises vision: dropped from the observer only, and still
    // worth naming, otherwise the observer's pick silently empties with no explanation.
    [Fact]
    public async Task RefreshOllamaModels_DropsTheObserversPick_WhenTheModelStopsAdvertisingVision()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("llama3.1")]);
        store.Setup(s => s.LoadLlmModel(ModelRole.Mutate, ModelProvider.Local)).Returns("llama3.1");
        store.Setup(s => s.LoadLlmModel(ModelRole.Observe, ModelProvider.Local)).Returns("llama3.1");

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        store.Verify(s => s.PersistLlmModel(ModelRole.Observe, ModelProvider.Local, string.Empty), Times.Once,
            "it can no longer serve a vision request");
        store.Verify(s => s.PersistLlmModel(ModelRole.Mutate, ModelProvider.Local, It.IsAny<string?>()), Times.Never,
            "it is still installed for text");
        vm.StatusKind.Should().Be(StatusKind.Warning);
        vm.StatusMessage.Should().Contain("llama3.1");
    }

    [Fact]
    public async Task RefreshOllamaModels_WhenTheServerIsDown_LeavesThePicksAndListIntact()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        store.Setup(s => s.LoadLlmModel(ModelRole.Mutate, ModelProvider.Local)).Returns("llama3.1");
        vm.Settings.OllamaModels.Add("llama3.1");

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.Settings.OllamaModels.Should().ContainSingle().Which.Should().Be("llama3.1");
        vm.StatusKind.Should().Be(StatusKind.Error);
        store.Verify(s => s.PersistLlmModel(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>(), It.IsAny<string?>()),
            Times.Never, "an unreachable server proves nothing about the model");
    }

    // A reachable server answering with zero models looks identical to a misconfigured URL or a
    // proxy answering for one, so it must not be treated as proof a pick is gone.
    [Fact]
    public async Task RefreshOllamaModels_WhenTheServerListsNothing_LeavesThePicksIntact()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(s => s.LoadLlmModel(ModelRole.Mutate, ModelProvider.Local)).Returns("llama3.1");

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.StatusKind.Should().Be(StatusKind.Warning);
        store.Verify(s => s.PersistLlmModel(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshOllamaModels_CachesTheListForTheNextLaunch()
    {
        var vm = OllamaVm(out var catalog, out var store);
        vm.Settings.OllamaBaseUrl = "http://fireengine:11434";
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("llama3.1"), Model("gemma3:27b", vision: true)]);

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

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

        await vm.Settings.EnsureOllamaModelsLoadedAsync();
        await vm.Settings.EnsureOllamaModelsLoadedAsync();

        catalog.Verify(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "every page appearance must not re-probe the server");
        vm.Settings.OllamaModels.Should().Contain("llama3.1");
        vm.StatusMessage.Should().BeNullOrEmpty("the automatic load is not a user action worth narrating");
    }

    [Fact]
    public void LoadSavedUiState_SeedsThePickersFromTheCachedList()
    {
        _mockUiStateStore.Setup(s => s.LoadOllamaModels(It.IsAny<string>()))
            .Returns((string[])["llama3.1", "gemma3:27b"]);
        _mockUiStateStore.Setup(s => s.LoadOllamaVisionModels(It.IsAny<string>()))
            .Returns((string[])["gemma3:27b"]);

        _viewModel.LoadSavedUiState();

        _viewModel.Settings.OllamaModels.Should().BeEquivalentTo((string[])["llama3.1", "gemma3:27b"],
            "the picker is usable at launch, and for the whole session if the server is down");
        _viewModel.Settings.OllamaVisionModels.Should().BeEquivalentTo(["gemma3:27b"]);
    }

    [Fact]
    public async Task RefreshOllamaModels_WhenTheHostIsNotOllama_BlamesTheServerNotTheConnection()
    {
        var vm = OllamaVm(out var catalog, out _);
        vm.Settings.OllamaBaseUrl = "http://192.168.178.26:47832/v1";
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OllamaEndpointException("http://192.168.178.26:47832/v1/api/tags", 404));

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        var said = vm.Settings.OllamaServerStatus;
        said.Should().NotBeNull();
        said.Should().NotContain("Couldn't reach", "the host answered; an unreachable message sends the user after a cable");
        said.Should().Contain("HTTP 404").And.Contain("/api/tags").And.Contain("not an Ollama server");
        said.Should().Contain("/v1", "pasting an OpenAI-compatible root IS the mistake being made");
        vm.StatusKind.Should().Be(StatusKind.Error);
    }

    [Fact]
    public async Task RefreshOllamaModels_WhenTheConnectionIsRefused_StillReportsUnreachable()
    {
        var vm = OllamaVm(out var catalog, out _);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("target machine actively refused it"));

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.Settings.OllamaServerStatus.Should().StartWith("Couldn't reach Ollama at ")
            .And.NotContain("/api/tags", "nothing answered, so no route can be blamed");
    }

    // ---- Inline card status: the failure the user could not see -----------------------------
    // Pointing the Ollama URL at a server that is merely *not* Ollama (an OpenAI-compatible host
    // answers; /api/tags just 404s) used to produce a WARN in the log and nothing on screen:
    // StatusMessage is drawn only by MainPage.xaml, while every one of these buttons lives on
    // Settings or a sibling page. So the reporting is asserted on the card property now.

    [Fact]
    public async Task RefreshOllamaModels_WhenTheAnswerIsNotOllama_ReportsItOnTheCard()
    {
        var vm = OllamaVm(out var catalog, out _);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ollama returned HTTP 404 listing models."));

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.Settings.OllamaServerStatus.Should().NotBeNull("a pressed button must answer on its own page");
        vm.Settings.OllamaServerStatus.Should().Contain("404");
        vm.StatusKind.Should().Be(StatusKind.Error);
    }

    [Fact]
    public async Task RefreshOllamaModels_WhenModelsAreFound_ReportsTheCountOnTheCard()
    {
        var vm = OllamaVm(out var catalog, out _);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("llama3.1"), Model("gemma3:27b", vision: true)]);

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        // The card and the status bar carry one sentence, never two variants of it.
        vm.Settings.OllamaServerStatus.Should().Be(vm.StatusMessage);
        vm.Settings.OllamaServerStatus.Should().Contain("2 Ollama model(s)").And.Contain("1 with vision");
        vm.StatusKind.Should().Be(StatusKind.Success);
    }

    [Fact]
    public async Task RefreshOllamaModels_WhenNothingIsInstalled_ReportsItOnTheCard()
    {
        var vm = OllamaVm(out var catalog, out _);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        vm.Settings.OllamaServerStatus.Should().Be("No models installed on the Ollama server.");
    }

    // The silence this phase must NOT break: the session-start probe runs on page appearance, and a
    // down server nagging on every navigation is its own bug. Only an explicit press reports.
    [Fact]
    public async Task EnsureOllamaModelsLoaded_WhenTheServerIsDown_StaysSilentOnTheCardToo()
    {
        var vm = OllamaVm(out var catalog, out _);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unreachable"));

        await vm.Settings.EnsureOllamaModelsLoadedAsync();

        vm.Settings.OllamaServerStatus.Should().BeNull("the automatic probe must not announce");
    }

    [Fact]
    public async Task OllamaBaseUrl_WhenEdited_RetiresThePreviousVerdict()
    {
        var vm = OllamaVm(out var catalog, out _);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Ollama returned HTTP 404 listing models."));
        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);
        vm.Settings.OllamaServerStatus.Should().NotBeNullOrEmpty();

        vm.Settings.OllamaBaseUrl = "http://localhost:11434";

        vm.Settings.OllamaServerStatus.Should().BeNull(
            "a corrected URL must not still read \"still not Ollama\" from the URL it replaces");
    }

    [Fact]
    public async Task RefreshOpenRouterVisionModels_WhenItThrows_ReportsItOnTheCard()
    {
        _mockOpenRouterModelCatalog
            .Setup(c => c.ListVisionModelsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("HTTP 401"));

        await _viewModel.Settings.RefreshOpenRouterVisionModelsCommand.ExecuteAsync(null);

        _viewModel.Settings.OpenRouterVisionStatus.Should().Contain("401");
        _viewModel.Settings.OpenRouterVisionStatus.Should().Be(_viewModel.StatusMessage);
        _viewModel.StatusKind.Should().Be(StatusKind.Error);
    }

    // ---- Role picks on a refresh ---------------------------------------------------------
    // The roles' Local picks live in the store (each row's ModelChoiceState writes them); the refresh
    // drops a tag the server no longer has and leaves every other pick alone.

    [Fact]
    public async Task RefreshOllamaModels_ClearsEveryRoleThatPickedAGoneModel()
    {
        var vm = OllamaVm(out var catalog, out var store);
        catalog.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Model("gemma3:27b", vision: true)]);
        store.Setup(s => s.LoadLlmModel(ModelRole.Mutate, ModelProvider.Local)).Returns("deleted-on-server");
        store.Setup(s => s.LoadLlmModel(ModelRole.Enrich, ModelProvider.Local)).Returns("gemma3:27b");

        await vm.Settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        store.Verify(s => s.PersistLlmModel(ModelRole.Mutate, ModelProvider.Local, string.Empty), Times.Once,
            "a role naming a model the server dropped fails later as an opaque 404");
        store.Verify(s => s.PersistLlmModel(ModelRole.Enrich, ModelProvider.Local, It.IsAny<string?>()), Times.Never,
            "the other role picked a model still installed");
        vm.StatusMessage.Should().Contain("deleted-on-server").And.Contain("no longer installed");
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
