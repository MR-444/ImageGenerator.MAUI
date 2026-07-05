using FluentAssertions;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Moq;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Application.Services;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.ViewModels;

[Collection("OutputPathsState")]
public class GeneratorViewModelTests
{
    private const string FakeSavePath = @"C:\fake\generated.png";

    private readonly GeneratorViewModel _viewModel;
    private readonly Mock<IJobRunner> _mockJobRunner;
    private readonly Mock<IApiTokenStore> _mockTokenStore;
    private readonly Mock<IPollinationsTokenStore> _mockPollinationsTokenStore;
    private readonly Mock<IComfyUiAuthStore> _mockComfyUiAuthStore;
    private readonly Mock<ICivitaiTokenStore> _mockCivitaiTokenStore;
    private readonly Mock<IAnthropicTokenStore> _mockAnthropicTokenStore;
    private readonly Mock<ICivitaiPostingService> _mockCivitaiPostingService;
    private readonly Mock<IUiStateStore> _mockUiStateStore;
    private readonly Mock<IModelCatalogCoordinator> _mockCatalogCoordinator;
    private readonly Mock<IPromptBatchParser> _mockPromptBatchParser;
    private readonly Mock<IComfyUiCheckpointService> _mockCheckpointService;
    private readonly Mock<IGalleryService> _mockGalleryService;
    private readonly Mock<IFolderPicker> _mockFolderPicker;
    private readonly Mock<IOpenRouterModelCatalog> _mockOpenRouterModelCatalog;

    public GeneratorViewModelTests()
    {
        _mockJobRunner = new Mock<IJobRunner>();
        _mockTokenStore = new Mock<IApiTokenStore>();
        _mockPollinationsTokenStore = new Mock<IPollinationsTokenStore>();
        _mockComfyUiAuthStore = new Mock<IComfyUiAuthStore>();
        _mockCivitaiTokenStore = new Mock<ICivitaiTokenStore>();
        _mockAnthropicTokenStore = new Mock<IAnthropicTokenStore>();
        _mockCivitaiPostingService = new Mock<ICivitaiPostingService>();
        _mockUiStateStore = new Mock<IUiStateStore>();
        _mockCatalogCoordinator = new Mock<IModelCatalogCoordinator>();
        _mockPromptBatchParser = new Mock<IPromptBatchParser>();
        _mockCheckpointService = new Mock<IComfyUiCheckpointService>();
        _mockGalleryService = new Mock<IGalleryService>();
        _mockFolderPicker = new Mock<IFolderPicker>();
        _mockOpenRouterModelCatalog = new Mock<IOpenRouterModelCatalog>();

        _viewModel = new GeneratorViewModel(
            _mockJobRunner.Object,
            _mockTokenStore.Object,
            _mockPollinationsTokenStore.Object,
            _mockComfyUiAuthStore.Object,
            _mockCivitaiTokenStore.Object,
            _mockAnthropicTokenStore.Object,
            _mockCivitaiPostingService.Object,
            _mockUiStateStore.Object,
            _mockCatalogCoordinator.Object,
            ModelDescriptorRegistry.Default(),
            _mockPromptBatchParser.Object,
            _mockCheckpointService.Object,
            _mockGalleryService.Object,
            _mockFolderPicker.Object,
            NullLogger<GeneratorViewModel>.Instance,
            openRouterModelCatalog: _mockOpenRouterModelCatalog.Object);
    }

    [Fact]
    public void AllModels_ShouldContainExpectedSeed()
    {
        var values = _viewModel.ProviderFilter.AllModels.Select(m => m.Value).ToList();
        values.Should().Contain(ModelConstants.OpenAI.GptImage15OnReplicate);
        values.Should().Contain(ModelConstants.Flux.Pro11);
        values.Should().Contain(ModelConstants.Flux.Pro11Ultra);
        values.Should().Contain(ModelConstants.Flux.Klein4b);
        values.Should().Contain(ModelConstants.Google.NanoBanana2);
    }

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

    // --- Free GPU memory after rendering ---

    private GeneratorViewModel BuildViewModelWithVram(IComfyUiVramService vram) =>
        new(_mockJobRunner.Object, _mockTokenStore.Object, _mockPollinationsTokenStore.Object,
            _mockComfyUiAuthStore.Object, _mockCivitaiTokenStore.Object, _mockAnthropicTokenStore.Object,
            _mockCivitaiPostingService.Object, _mockUiStateStore.Object, _mockCatalogCoordinator.Object,
            ModelDescriptorRegistry.Default(), _mockPromptBatchParser.Object, _mockCheckpointService.Object,
            _mockGalleryService.Object, _mockFolderPicker.Object, NullLogger<GeneratorViewModel>.Instance,
            ollamaModelCatalog: null, comfyVram: vram);

    [Fact]
    public async Task MaybeFreeComfyUiVram_FreesOnlyWhenEnabled_ComfyModel_AndIdle()
    {
        var vram = new StubComfyVram();
        var vm = BuildViewModelWithVram(vram);
        vm.FreeVramAfterRendering = true;

        await vm.MaybeFreeComfyUiVramAsync("comfyui/anything");
        vram.Calls.Should().Be(1, "enabled + ComfyUI model + no batch running");

        await vm.MaybeFreeComfyUiVramAsync(ModelConstants.Flux.Pro11);
        vram.Calls.Should().Be(1, "non-ComfyUI models hold no local VRAM");

        vm.FreeVramAfterRendering = false;
        await vm.MaybeFreeComfyUiVramAsync("comfyui/anything");
        vram.Calls.Should().Be(1, "the toggle gates it off");
    }

    [Fact]
    public async Task MaybeFreeComfyUiVram_NoServiceWired_DoesNotThrow()
    {
        // The default _viewModel is built without a vram service (optional ctor arg).
        var act = async () => await _viewModel.MaybeFreeComfyUiVramAsync("comfyui/anything");
        await act.Should().NotThrowAsync();
    }

    private sealed class StubComfyVram : IComfyUiVramService
    {
        public int Calls { get; private set; }
        public Task TryFreeAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Providers_ShouldIncludeAllAndDistinctProviders()
    {
        // Default() now mirrors the DI set (audit fix), so the seeds span both hosted providers.
        _viewModel.ProviderFilter.Providers.Should().BeEquivalentTo(["All providers", "Replicate", "Pollinations"]);
    }

    [Fact]
    public void SelectedProvider_WhenSet_FiltersModels()
    {
        // Put two providers in play so selecting one is observably narrowing.
        _viewModel.ProviderFilter.ApplyCatalog(
        [
            new ModelOption("Flux 1.1 Pro", ModelConstants.Flux.Pro11, "Replicate"),
            new ModelOption("Pollinations Flux", ModelConstants.Pollinations.Flux, "Pollinations"),
        ]);

        _viewModel.ProviderFilter.SelectedProvider = "Replicate";

        _viewModel.ProviderFilter.FilteredModels.Should().OnlyContain(m => m.Provider == "Replicate");
        _viewModel.ProviderFilter.FilteredModels.Should().ContainSingle();
    }

    [Fact]
    public void SelectedModel_WhenChanged_UpdatesParametersModel()
    {
        var target = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);

        _viewModel.ProviderFilter.SelectedModel = target;

        _viewModel.Parameters.Model.Should().Be(ModelConstants.Flux.Klein4b);
    }

    // --- GPU gate: a ComfyUI render holds the shared fireEngine permit; cloud providers don't ---

    private GeneratorViewModel BuildGatedViewModel(IGpuGate gate) =>
        new(
            _mockJobRunner.Object,
            _mockTokenStore.Object,
            _mockPollinationsTokenStore.Object,
            _mockComfyUiAuthStore.Object,
            _mockCivitaiTokenStore.Object,
            _mockAnthropicTokenStore.Object,
            _mockCivitaiPostingService.Object,
            _mockUiStateStore.Object,
            _mockCatalogCoordinator.Object,
            ModelDescriptorRegistry.Default(),
            _mockPromptBatchParser.Object,
            _mockCheckpointService.Object,
            _mockGalleryService.Object,
            _mockFolderPicker.Object,
            NullLogger<GeneratorViewModel>.Instance,
            ollamaModelCatalog: null,
            comfyVram: null,
            gpuGate: gate);

    [Fact]
    public async Task GenerateImage_ComfyUiModel_SameHostAsOllama_AcquiresAndReleasesGpuGate()
    {
        var gate = new FakeGpuGate();
        var vm = BuildGatedViewModel(gate);
        vm.ComfyUiBaseUrl = "http://fireEngine:8188";   // note the case difference vs the Ollama default
        vm.OllamaBaseUrl = "http://fireengine:11434";
        vm.Parameters.Model = "comfyui/test";            // tokenless local provider
        vm.Parameters.Prompt = "a cat";

        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await ((IAsyncRelayCommand)vm.GenerateImageCommand).ExecuteAsync(null);

        gate.Acquired.Should().Be(1, "the render must hold the local GPU while it runs");
        gate.Released.Should().Be(1, "the gate is released after the post-render VRAM free");
    }

    [Fact]
    public async Task GenerateImage_NonComfyUiModel_DoesNotAcquireGpuGate()
    {
        var gate = new FakeGpuGate();
        var vm = BuildGatedViewModel(gate);
        vm.ComfyUiBaseUrl = "http://fireengine:8188";
        vm.OllamaBaseUrl = "http://fireengine:11434";
        vm.Parameters.Model = ModelConstants.Flux.Pro11;  // Replicate — never touches fireEngine's VRAM
        vm.Parameters.ApiToken = "valid-token";
        vm.Parameters.Prompt = "a cat";

        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await ((IAsyncRelayCommand)vm.GenerateImageCommand).ExecuteAsync(null);

        gate.Acquired.Should().Be(0, "cloud providers don't contend for the local GPU");
    }

    private sealed class FakeGpuGate : IGpuGate
    {
        public int Acquired { get; private set; }
        public int Released { get; private set; }

        public Task<IDisposable> AcquireAsync(CancellationToken ct = default)
        {
            Acquired++;
            return Task.FromResult<IDisposable>(new Lease(() => Released++));
        }

        private sealed class Lease(Action onRelease) : IDisposable
        {
            private Action? _onRelease = onRelease;
            public void Dispose() => Interlocked.Exchange(ref _onRelease, null)?.Invoke();
        }
    }

    [Fact]
    public async Task GenerateImage_WithValidParameters_ShouldGenerateImage()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";

        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.Jobs.Should().HaveCount(1);
        _viewModel.Jobs[0].StatusMessage.Should().StartWith("Saved to ");
        _viewModel.Jobs[0].StatusKind.Should().Be(StatusKind.Success);
        _viewModel.Jobs[0].ResultPath.Should().NotBeNull();
        _viewModel.Jobs[0].IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateImage_SavedOutcome_SetsLatestCompletedJob()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        // Drives the featured (large uncropped) queue card on the main page.
        _viewModel.LatestCompletedJob.Should().BeSameAs(_viewModel.Jobs[0]);
    }

    [Fact]
    public async Task GenerateImage_SavedOutcome_FeaturesNewestJob()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.Jobs[0].IsFeatured.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateImage_SecondSave_MovesFeaturedFlagToNewest()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);
        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        // Newest is Jobs[0]; the previous newest (now Jobs[1]) must lose the crown so exactly
        // one card renders the large preview.
        _viewModel.Jobs[0].IsFeatured.Should().BeTrue();
        _viewModel.Jobs[1].IsFeatured.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateImage_FailedOutcome_LeavesLatestCompletedJobNull()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Failed, null, "boom"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        // A failed/canceled job must never become the featured image.
        _viewModel.LatestCompletedJob.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImage_WithMissingFields_ShouldShowError()
    {
        _viewModel.Parameters.ApiToken = "";
        _viewModel.Parameters.Prompt = "";

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.StatusMessage.Should().Contain("API Token");
        _viewModel.StatusMessage.Should().Contain("Prompt");
        _viewModel.StatusKind.Should().Be(StatusKind.Error);
        _viewModel.Jobs.Should().BeEmpty();
    }

    [Fact]
    public void UpdateCustomAspectRatio_WhenCustomSelected_ShouldEnableCustomInput()
    {
        _viewModel.Parameters.AspectRatio = "custom";

        _viewModel.IsCustomAspectRatio.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithEmptyToken_ShouldSetInvalid()
    {
        _viewModel.Parameters.ApiToken = "";
        _viewModel.Parameters.Prompt = "anything";

        _viewModel.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithEmptyPrompt_ShouldSetInvalid()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "";

        _viewModel.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithTokenAndPrompt_ShouldSetValid()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "a cat on a sofa";

        _viewModel.IsValid.Should().BeTrue();
    }

    [Fact]
    public void AddImage_OnFlux2Model_AutoSelectsMatchInputImage()
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);
        _viewModel.AspectRatioOptions.Should().Contain("match_input_image");

        _viewModel.InputImages.SelectedImages.Add(FakeImage("a"));

        _viewModel.Parameters.AspectRatio.Should().Be("match_input_image");
    }

    [Fact]
    public void RemoveLastImage_OnFlux2Model_FallsBackToFirstAspectRatio()
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);
        _viewModel.InputImages.SelectedImages.Add(FakeImage("a"));
        _viewModel.Parameters.AspectRatio.Should().Be("match_input_image");

        _viewModel.InputImages.SelectedImages.Clear();

        _viewModel.Parameters.AspectRatio.Should().NotBe("match_input_image");
    }

    private static InputImagesCoordinator.InputImageItem FakeImage(string base64 = "abc", string name = "test.png") =>
        new(base64, null, name);

    [Fact]
    public void SelectedModel_Changed_AdjustsAspectRatioOptionsToSupportedList()
    {
        // Switching from Flux 1.1 Pro (has "custom") to Klein 4B (no "custom") must narrow the list.
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);

        _viewModel.AspectRatioOptions.Should().NotContain("custom");
        _viewModel.AspectRatioOptions.Should().Contain("1:1");
    }

    [Fact]
    public async Task GenerateImage_WhenJobRunnerThrowsException_ShouldHandleError()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ThrowsAsync(new Exception("Test error"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.Jobs.Should().HaveCount(1);
        _viewModel.Jobs[0].StatusMessage.Should().Be("Error: Test error");
        _viewModel.Jobs[0].StatusKind.Should().Be(StatusKind.Error);
        _viewModel.Jobs[0].ResultPath.Should().BeNull();
        _viewModel.Jobs[0].IsRunning.Should().BeFalse();
    }

    [Theory]
    [InlineData(ModelConstants.Flux.Pro11,                  true,  true,  true,  true,  true,  true)]
    [InlineData(ModelConstants.Flux.Pro11Ultra,             true,  false, false, true,  true,  true)]
    [InlineData(ModelConstants.Flux.Klein4b,                false, false, true,  true,  true,  true)]
    [InlineData(ModelConstants.OpenAI.GptImage15OnReplicate, false, false, true, true,  false, true)]
    [InlineData(ModelConstants.Google.NanoBanana2,          false, false, false, true,  false, true)]
    public void Capabilities_MatchExpectedMatrixPerModel(
        string modelValue,
        bool safety,
        bool upsampling,
        bool outputQuality,
        bool aspectRatio,
        bool seed,
        bool imagePrompt)
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == modelValue);

        _viewModel.Capabilities.SafetyTolerance.Should().Be(safety);
        _viewModel.Capabilities.PromptUpsampling.Should().Be(upsampling);
        _viewModel.Capabilities.OutputQuality.Should().Be(outputQuality);
        _viewModel.Capabilities.AspectRatio.Should().Be(aspectRatio);
        _viewModel.Capabilities.Seed.Should().Be(seed);
        _viewModel.Capabilities.ImagePrompt.Should().Be(imagePrompt);
    }

    [Fact]
    public void Capabilities_NanoBanana2_ExposesResolution()
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Google.NanoBanana2);

        _viewModel.SupportsResolution.Should().BeTrue();
        _viewModel.ResolutionOptions.Should().BeEquivalentTo("1K", "2K", "4K");
    }

    [Fact]
    public void Capabilities_GptImage15_ExposesAllFourGptOptionLists()
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.OpenAI.GptImage15OnReplicate);

        _viewModel.SupportsGptQuality.Should().BeTrue();
        _viewModel.Capabilities.GptBackgroundOptions.Should().NotBeNull();
        _viewModel.Capabilities.GptModerationOptions.Should().NotBeNull();
        _viewModel.Capabilities.GptInputFidelityOptions.Should().NotBeNull();
        _viewModel.GptQualityOptions.Should().BeEquivalentTo("auto", "low", "medium", "high");
        _viewModel.GptBackgroundOptions.Should().BeEquivalentTo("auto", "transparent", "opaque");
        _viewModel.GptModerationOptions.Should().BeEquivalentTo("auto", "low");
        _viewModel.GptInputFidelityOptions.Should().BeEquivalentTo("low", "high");
    }

    [Fact]
    public void Capabilities_NonGptModel_HidesGptOptionLists()
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);

        _viewModel.SupportsGptQuality.Should().BeFalse();
        _viewModel.Capabilities.GptBackgroundOptions.Should().BeNull();
        _viewModel.SupportsResolution.Should().BeFalse();
    }

    [Fact]
    public void Capabilities_FluxKlein4b_ReturnsTwelveAspectRatios()
    {
        var caps = ModelDescriptorRegistry.Default().CapabilitiesFor(ModelConstants.Flux.Klein4b).Capabilities;

        caps.AspectRatios.Should().HaveCount(12).And.Contain("match_input_image");
        caps.SafetyTolerance.Should().BeFalse();
        caps.PromptUpsampling.Should().BeFalse();
        caps.Seed.Should().BeTrue();
        caps.ImagePrompt.Should().BeTrue();
    }

    [Fact]
    public void Capabilities_GptImage15OnReplicate_ReturnsThreeAspectRatios()
    {
        var caps = ModelDescriptorRegistry.Default().CapabilitiesFor(ModelConstants.OpenAI.GptImage15OnReplicate).Capabilities;

        caps.AspectRatios.Should().BeEquivalentTo(["1:1", "3:2", "2:3"]);
        caps.AspectRatioLabel.Should().Be("Aspect ratio");
        caps.Seed.Should().BeFalse();
        caps.ImagePrompt.Should().BeTrue();
    }

    [Fact]
    public void Width_WhenSetBelowMin_IsClampedToMin()
    {
        _viewModel.Parameters.Width = 10;
        _viewModel.Parameters.Width.Should().Be(ValidationConstants.ImageWidthMin);
    }

    [Fact]
    public void Width_WhenSetAboveMax_IsClampedToMax()
    {
        _viewModel.Parameters.Width = 999_999;
        _viewModel.Parameters.Width.Should().Be(ValidationConstants.ImageWidthMax);
    }

    [Fact]
    public void Height_WhenSetBelowMin_IsClampedToMin()
    {
        _viewModel.Parameters.Height = 0;
        _viewModel.Parameters.Height.Should().Be(ValidationConstants.ImageHeightMin);
    }

    [Fact]
    public void Height_WhenSetAboveMax_IsClampedToMax()
    {
        _viewModel.Parameters.Height = 10_000;
        _viewModel.Parameters.Height.Should().Be(ValidationConstants.ImageHeightMax);
    }

    [Fact]
    public async Task RefreshModels_Success_IngestsMergedListAndReportsSuccess()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        var merged = new List<ModelOption>
        {
            new("flux-2", "black-forest-labs/flux-2", "Replicate"),
            new("gpt-image-1.5", "openai/gpt-image-1.5", "Replicate")
        };
        _mockCatalogCoordinator
            .Setup(x => x.RefreshAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged);

        await ((IAsyncRelayCommand)_viewModel.RefreshModelsCommand).ExecuteAsync(null);

        _viewModel.ProviderFilter.AllModels.Select(m => m.Value)
            .Should().Contain("black-forest-labs/flux-2")
            .And.Contain("openai/gpt-image-1.5");
        _viewModel.StatusKind.Should().Be(StatusKind.Success);
    }

    [Fact]
    public async Task LoadCachedCatalog_WithMergedResult_IngestsModelsAndProviders()
    {
        var merged = new List<ModelOption>
        {
            new("flux-2-pro", "black-forest-labs/flux-2-pro", "Replicate"),
            new("gpt-image-1.5", "openai/gpt-image-1.5", "Replicate")
        };
        _mockCatalogCoordinator
            .Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged);

        await _viewModel.LoadCachedCatalogAsync();

        _viewModel.ProviderFilter.AllModels.Select(m => m.Value)
            .Should().Contain("black-forest-labs/flux-2-pro")
            .And.Contain("openai/gpt-image-1.5");
        _viewModel.ProviderFilter.Providers.Should().Contain("Replicate");
    }

    [Fact]
    public async Task LoadCachedCatalog_WhenCoordinatorReturnsNull_KeepsHardcodedSeed()
    {
        var seed = _viewModel.ProviderFilter.AllModels.ToList();
        _mockCatalogCoordinator
            .Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModelOption>?)null);

        await _viewModel.LoadCachedCatalogAsync();

        _viewModel.ProviderFilter.AllModels.Should().BeEquivalentTo(seed);
    }

    [Fact]
    public async Task LoadCachedCatalog_RunningTwice_IgnoresSecondInvocation()
    {
        var merged = new List<ModelOption>
        {
            new("flux-2", "black-forest-labs/flux-2", "Replicate")
        };
        _mockCatalogCoordinator
            .Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged);

        await _viewModel.LoadCachedCatalogAsync();
        await _viewModel.LoadCachedCatalogAsync();

        _mockCatalogCoordinator.Verify(
            x => x.LoadCachedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshModels_Success_UpdatesFilteredModelsFromAllModels()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        var merged = new List<ModelOption>
        {
            new("flux-2-pro", "black-forest-labs/flux-2-pro", "Replicate"),
            new("gpt-image-1.5", "openai/gpt-image-1.5", "Replicate"),
            new("nano-banana-2", "google/nano-banana-2", "Replicate")
        };
        _mockCatalogCoordinator
            .Setup(x => x.RefreshAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged);

        await ((IAsyncRelayCommand)_viewModel.RefreshModelsCommand).ExecuteAsync(null);

        // Regression: FilteredModels must reflect freshly-applied AllModels, not be stuck on the
        // pre-refresh list.
        _viewModel.ProviderFilter.FilteredModels.Select(m => m.Value)
            .Should().Contain("black-forest-labs/flux-2-pro")
            .And.Contain("openai/gpt-image-1.5")
            .And.Contain("google/nano-banana-2");
        _viewModel.ProviderFilter.Providers.Should().Contain("Replicate");
    }

    [Fact]
    public async Task RefreshModels_EmptyToken_StillFetchesForPollinations()
    {
        // Pollinations' /models endpoint is anonymous, so an empty Replicate token no longer
        // gates a refresh: the coordinator is still called, Replicate yields nothing, and
        // any Pollinations result still hydrates the catalog.
        _viewModel.Parameters.ApiToken = "";
        var pollinationsOnly = new List<ModelOption>
        {
            new("Flux (Pollinations)", ModelConstants.Pollinations.Flux, ProviderConstants.Pollinations)
        };
        _mockCatalogCoordinator
            .Setup(x => x.RefreshAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pollinationsOnly);

        await ((IAsyncRelayCommand)_viewModel.RefreshModelsCommand).ExecuteAsync(null);

        _mockCatalogCoordinator.Verify(x => x.RefreshAsync("", It.IsAny<CancellationToken>()), Times.Once);
        _viewModel.StatusKind.Should().Be(StatusKind.Success);
        _viewModel.ProviderFilter.AllModels.Select(m => m.Value).Should().Contain(ModelConstants.Pollinations.Flux);
    }

    [Fact]
    public async Task RefreshModels_NullResult_KeepsExistingCatalogAndReportsError()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        var originalModels = _viewModel.ProviderFilter.AllModels.ToList();
        _mockCatalogCoordinator
            .Setup(x => x.RefreshAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModelOption>?)null);

        await ((IAsyncRelayCommand)_viewModel.RefreshModelsCommand).ExecuteAsync(null);

        _viewModel.ProviderFilter.AllModels.Should().BeEquivalentTo(originalModels);
        _viewModel.StatusKind.Should().Be(StatusKind.Error);
    }

    [Fact]
    public async Task GenerateImage_WhenJobRunnerReturnsCanceledMessage_ShouldSetCanceledKind()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Failed, null, "Image generation was canceled."));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.Jobs.Should().HaveCount(1);
        _viewModel.Jobs[0].StatusKind.Should().Be(StatusKind.Canceled);
        _viewModel.Jobs[0].ResultPath.Should().BeNull();
    }

    [Fact]
    public void TokenProvider_ValueChange_PersistsAndForwardsToParameters()
    {
        var replicate = _viewModel.TokenProviders.Single(p => p.Key == "replicate");

        replicate.Value = "fresh-token";

        _mockTokenStore.Verify(x => x.Persist("fresh-token"), Times.Once);
        _viewModel.Parameters.ApiToken.Should().Be("fresh-token");
    }

    [Fact]
    public void TokenProvider_PollinationsValueChange_PersistsToPollinationsStore()
    {
        var pollinations = _viewModel.TokenProviders.Single(p => p.Key == "pollinations");

        pollinations.Value = "poll-token";

        _mockPollinationsTokenStore.Verify(x => x.Persist("poll-token"), Times.Once);
        _viewModel.Parameters.PollinationsApiToken.Should().Be("poll-token");
        // Replicate store must stay untouched — token slots are independent.
        _mockTokenStore.Verify(x => x.Persist("poll-token"), Times.Never);
    }

    [Fact]
    public void ForgetSelectedToken_ClearsActiveProviderOnly()
    {
        // Default selected provider is "replicate" — set both tokens, forget Replicate,
        // and assert the Pollinations slot is untouched.
        _viewModel.TokenProviders.Single(p => p.Key == "replicate").Value = "some-token";
        _viewModel.TokenProviders.Single(p => p.Key == "pollinations").Value = "poll-token";

        _viewModel.ForgetSelectedTokenCommand.Execute(null);

        _viewModel.Parameters.ApiToken.Should().BeEmpty();
        _viewModel.Parameters.PollinationsApiToken.Should().Be("poll-token");
        _mockTokenStore.Verify(x => x.Forget(), Times.Once);
        _mockPollinationsTokenStore.Verify(x => x.Forget(), Times.Never);
    }

    [Fact]
    public async Task LoadAllTokensAsync_PopulatesEveryProviderFromItsOwnStore()
    {
        _mockTokenStore.Setup(x => x.LoadAsync()).ReturnsAsync("saved-replicate");
        _mockPollinationsTokenStore.Setup(x => x.LoadAsync()).ReturnsAsync("saved-pollinations");

        await _viewModel.LoadAllTokensAsync();

        _viewModel.Parameters.ApiToken.Should().Be("saved-replicate");
        _viewModel.Parameters.PollinationsApiToken.Should().Be("saved-pollinations");
        _viewModel.TokenProviders.Single(p => p.Key == "replicate").Value.Should().Be("saved-replicate");
        _viewModel.TokenProviders.Single(p => p.Key == "pollinations").Value.Should().Be("saved-pollinations");
    }

    [Fact]
    public async Task LoadAllTokensAsync_IsIdempotent_WhenCalledTwice()
    {
        _mockTokenStore.Setup(x => x.LoadAsync()).ReturnsAsync("saved-replicate");
        _mockPollinationsTokenStore.Setup(x => x.LoadAsync()).ReturnsAsync("saved-pollinations");

        await _viewModel.LoadAllTokensAsync();
        await _viewModel.LoadAllTokensAsync();

        _mockTokenStore.Verify(x => x.LoadAsync(), Times.Once);
        _mockPollinationsTokenStore.Verify(x => x.LoadAsync(), Times.Once);
    }

    [Fact]
    public async Task TokenProvider_ComfyUiAuthRow_LoadsAndPersists_WithoutTouchingParameters()
    {
        // The ComfyUI services read the auth store directly per run — the value must never
        // land in ImageGenerationParameters (ComfyUI stays tokenless for validation).
        _mockComfyUiAuthStore.Setup(x => x.LoadAsync()).ReturnsAsync("Bearer saved");

        await _viewModel.LoadAllTokensAsync();
        var comfyui = _viewModel.TokenProviders.Single(p => p.Key == "comfyui");

        comfyui.Value.Should().Be("Bearer saved");
        comfyui.Value = "Bearer fresh";

        _mockComfyUiAuthStore.Verify(x => x.Persist("Bearer fresh"), Times.Once);
        _viewModel.Parameters.ApiToken.Should().BeEmpty();
        _viewModel.Parameters.PollinationsApiToken.Should().BeNullOrEmpty();
    }

    // --- Card title + strength gate state machine ---

    [Theory]
    [InlineData(ModelConstants.Flux.Pro11,                  "Input Image (optional)")]
    [InlineData(ModelConstants.Flux.Pro11Ultra,             "Input Image (optional)")]
    [InlineData(ModelConstants.Flux.Klein4b,                "Input Image (optional)")]
    [InlineData(ModelConstants.OpenAI.GptImage15OnReplicate, "Input Images (optional, up to 10)")]
    [InlineData(ModelConstants.Google.NanoBanana2,          "Input Images (optional, up to 14)")]
    public void ImagePromptCardTitle_ReflectsMaxImageInputs(string model, string expected)
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == model);

        _viewModel.InputImages.ImagePromptCardTitle.Should().Be(expected);
    }

    [Fact]
    public void SupportsImagePromptStrength_RequiresBothUltraModelAndAtLeastOneImage()
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11Ultra);
        _viewModel.InputImages.SupportsImagePromptStrength.Should().BeFalse("no image yet");

        _viewModel.InputImages.SelectedImages.Add(FakeImage("a"));
        _viewModel.InputImages.SupportsImagePromptStrength.Should().BeTrue();

        _viewModel.InputImages.SelectedImages.Clear();
        _viewModel.InputImages.SupportsImagePromptStrength.Should().BeFalse("image removed");
    }

    [Fact]
    public void SupportsImagePromptStrength_NonUltraModel_StaysFalseEvenWithImages()
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);
        _viewModel.InputImages.SelectedImages.Add(FakeImage("a"));

        _viewModel.InputImages.SupportsImagePromptStrength.Should().BeFalse();
    }

    // --- Multi-image commands ---

    [Fact]
    public async Task AddImageCommand_AtCap_SetsErrorStatus_AndDoesNotOpenPicker()
    {
        // Flux 1.1 Pro has MaxImageInputs = 1.
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);
        _viewModel.InputImages.SelectedImages.Add(FakeImage("a"));

        _viewModel.InputImages.CanAddImage.Should().BeFalse();
        await ((IAsyncRelayCommand)_viewModel.InputImages.AddImageCommand).ExecuteAsync(null);

        _viewModel.StatusKind.Should().Be(StatusKind.Error);
        _viewModel.StatusMessage.Should().Contain("Maximum");
        _viewModel.InputImages.SelectedImages.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveImageCommand_RemovesOnlyTargetedItem()
    {
        var a = FakeImage("a", "a.png");
        var b = FakeImage("b", "b.png");
        var c = FakeImage("c", "c.png");
        _viewModel.InputImages.SelectedImages.Add(a);
        _viewModel.InputImages.SelectedImages.Add(b);
        _viewModel.InputImages.SelectedImages.Add(c);

        _viewModel.InputImages.RemoveImageCommand.Execute(b);

        _viewModel.InputImages.SelectedImages.Should().ContainInOrder(a, c);
        _viewModel.InputImages.SelectedImages.Should().NotContain(b);
        // Audit finding: Remove used to blank the status line, wiping active warnings.
        _viewModel.StatusMessage.Should().Be("Removed b.png.");
        _viewModel.StatusKind.Should().Be(StatusKind.Info);
    }

    [Fact]
    public void ClearImagesCommand_EmptiesCollection()
    {
        _viewModel.InputImages.SelectedImages.Add(FakeImage("a"));
        _viewModel.InputImages.SelectedImages.Add(FakeImage("b"));

        _viewModel.InputImages.ClearImagesCommand.Execute(null);

        _viewModel.InputImages.SelectedImages.Should().BeEmpty();
        _viewModel.StatusMessage.Should().Be("Input images cleared.");
        _viewModel.StatusKind.Should().Be(StatusKind.Info);
    }

    [Fact]
    public void SwitchModelWithNarrowerCap_TruncatesSelectedImagesToNewCap()
    {
        // Start on nano-banana-2 (cap 14), load 5 images, switch to Flux 1.1 Pro (cap 1).
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Google.NanoBanana2);
        for (var i = 0; i < 5; i++) _viewModel.InputImages.SelectedImages.Add(FakeImage($"img{i}"));
        _viewModel.InputImages.SelectedImages.Should().HaveCount(5);

        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);

        _viewModel.InputImages.SelectedImages.Should().HaveCount(1);
        _viewModel.InputImages.CanAddImage.Should().BeFalse();
    }

    [Fact]
    public void CanAddImage_FlipsAtExactlyTheCap()
    {
        // gpt-image-1.5: cap is 10.
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.OpenAI.GptImage15OnReplicate);

        for (var i = 0; i < 9; i++)
        {
            _viewModel.InputImages.SelectedImages.Add(FakeImage($"img{i}"));
            _viewModel.InputImages.CanAddImage.Should().BeTrue($"only {i + 1} of 10 added");
        }

        _viewModel.InputImages.SelectedImages.Add(FakeImage("img10"));
        _viewModel.InputImages.CanAddImage.Should().BeFalse("reached cap");
    }

    [Fact]
    public void ImagePrompts_MirrorsSelectedImagesOnChange()
    {
        _viewModel.InputImages.SelectedImages.Add(FakeImage("a", "a.png"));
        _viewModel.InputImages.SelectedImages.Add(FakeImage("b", "b.png"));

        _viewModel.Parameters.ImagePrompts.Should().ContainInOrder("a", "b");

        _viewModel.InputImages.SelectedImages.RemoveAt(0);
        _viewModel.Parameters.ImagePrompts.Should().ContainInOrder("b").And.HaveCount(1);
    }

    [Fact]
    public async Task GenerateImage_TwoConcurrentCalls_EachJobKeepsItsOwnPromptSnapshot()
    {
        // Regression guard for the original bug: "image N ends up with image N+1's prompt".
        // Two generations must be isolated so that editing Parameters.Prompt between clicks
        // (or even mid-flight) never bleeds across jobs.
        _viewModel.Parameters.ApiToken = "valid-token";

        var gate1 = new TaskCompletionSource<JobOutcome>();
        var gate2 = new TaskCompletionSource<JobOutcome>();
        var call = 0;
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .Returns(() => Interlocked.Increment(ref call) == 1 ? gate1.Task : gate2.Task);

        _viewModel.Parameters.Prompt = "PROMPT_A";
        var run1 = ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.Parameters.Prompt = "PROMPT_B";
        var run2 = ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        // Both jobs must already be in the collection with frozen prompt snapshots,
        // before either runner call has completed.
        _viewModel.Jobs.Should().HaveCount(2);
        _viewModel.Jobs[0].Prompt.Should().Be("PROMPT_B"); // newest first (Insert at 0)
        _viewModel.Jobs[1].Prompt.Should().Be("PROMPT_A");
        _viewModel.Jobs[0].Parameters.Prompt.Should().Be("PROMPT_B");
        _viewModel.Jobs[1].Parameters.Prompt.Should().Be("PROMPT_A");

        // Release both tasks with a canceled-style outcome so RunJobAsync exits cleanly.
        gate1.SetResult(new JobOutcome(JobOutcomeKind.Failed, null, "canceled"));
        gate2.SetResult(new JobOutcome(JobOutcomeKind.Failed, null, "canceled"));
        await Task.WhenAll(run1, run2);
    }

    [Fact]
    public void AspectRatio_SwitchingToCompatibleModel_StaysPut()
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11Ultra);
        _viewModel.Parameters.AspectRatio = "21:9";

        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);

        _viewModel.Parameters.AspectRatio.Should().Be("21:9");
    }

    [Fact]
    public void AspectRatio_SwitchingToIncompatibleModelThenBack_RestoresPreferred()
    {
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);
        _viewModel.Parameters.AspectRatio = "21:9";

        // GPT 1.5's AR list is just ["1:1", "3:2", "2:3"] — 21:9 is rejected, AR falls back.
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.OpenAI.GptImage15OnReplicate);
        _viewModel.Parameters.AspectRatio.Should().NotBe("21:9");

        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);

        _viewModel.Parameters.AspectRatio.Should().Be("21:9", "the preferred AR is restored when the new model supports it again");
    }

    [Fact]
    public void AspectRatio_UserPickOverridesInitialDefault()
    {
        // Constructor seeds the initial AR (16:9) as the preferred. A user pick replaces it.
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);
        _viewModel.Parameters.AspectRatio = "1:1";

        // GPT 1.5 supports both 16:9 (no, actually only 1:1 / 3:2 / 2:3) and 1:1 — 1:1 must win.
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.OpenAI.GptImage15OnReplicate);

        _viewModel.Parameters.AspectRatio.Should().Be("1:1");
    }

    [Fact]
    public void AspectRatio_RemoveAllImages_FallsBackToPreferredWhenValid()
    {
        // Start on NanoBanana2 which supports both "16:9" and "match_input_image".
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Google.NanoBanana2);
        _viewModel.Parameters.AspectRatio = "16:9";

        _viewModel.InputImages.SelectedImages.Add(FakeImage("a"));
        _viewModel.Parameters.AspectRatio.Should().Be("match_input_image", "auto-select on 0→1 with a model that supports it");

        _viewModel.InputImages.SelectedImages.Clear();

        // Without sticky logic this would fall back to "1:1" (first non-match-input AR in NanoBanana2).
        _viewModel.Parameters.AspectRatio.Should().Be("16:9", "the user's preferred AR is restored when images are cleared");
    }

    [Fact]
    public void Prompt_WhenChanged_PersistsToUiStateStore()
    {
        _viewModel.Parameters.Prompt = "a serene mountain landscape";

        _mockUiStateStore.Verify(s => s.PersistPrompt("a serene mountain landscape"), Times.Once);
    }

    [Fact]
    public void Model_WhenChanged_PersistsToUiStateStore()
    {
        _viewModel.Parameters.Model = ModelConstants.Flux.Pro11;

        _mockUiStateStore.Verify(s => s.PersistModel(ModelConstants.Flux.Pro11), Times.Once);
    }

    [Fact]
    public void LoadSavedUiState_RestoresPromptAndModelFromStore()
    {
        _mockUiStateStore.Setup(s => s.LoadPrompt()).Returns("restored prompt");
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ModelConstants.Flux.Pro11);

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.Prompt.Should().Be("restored prompt");
        _viewModel.Parameters.Model.Should().Be(ModelConstants.Flux.Pro11);
    }

    [Fact]
    public void LoadSavedUiState_WhenSavedModelNotInCatalog_LeavesModelUnchanged()
    {
        var initialModel = _viewModel.Parameters.Model;
        _mockUiStateStore.Setup(s => s.LoadPrompt()).Returns((string?)null);
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns("openai/never-existed");

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.Model.Should().Be(initialModel);
    }

    [Fact]
    public void LoadSavedUiState_WhenStoreReturnsNullOrEmpty_LeavesParametersUntouched()
    {
        var initialPrompt = _viewModel.Parameters.Prompt;
        var initialModel = _viewModel.Parameters.Model;
        _mockUiStateStore.Setup(s => s.LoadPrompt()).Returns((string?)null);
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(string.Empty);

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.Prompt.Should().Be(initialPrompt);
        _viewModel.Parameters.Model.Should().Be(initialModel);
    }

    [Fact]
    public async Task LoadCachedCatalog_DoesNotPersistModelDuringHydrate()
    {
        // Regression for the "always reverts to flux-2-max" bug: ApplyCatalog reassigning
        // FilteredModels caused MAUI's Picker to reset SelectedItem to the first row, which
        // round-tripped through SelectedModel → Parameters.Model → PersistModel and clobbered
        // the user's saved value on every launch. The suppression flag must keep PersistModel
        // silent for the entire hydrate.
        var merged = new List<ModelOption>
        {
            new("flux-2-max", ModelConstants.Flux.Max2, "Replicate"),
            new("flux-1.1-pro", ModelConstants.Flux.Pro11, "Replicate"),
            new("gpt-image-2", ModelConstants.OpenAI.GptImage2OnReplicate, "Replicate"),
        };
        _mockCatalogCoordinator
            .Setup(c => c.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged);

        await _viewModel.LoadCachedCatalogAsync();

        _mockUiStateStore.Verify(s => s.PersistModel(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LoadSavedUiState_DoesNotPersistModel_OnRestoreOfSameValue()
    {
        // The redundant re-persist of a value we just read is wasted I/O; the suppression flag
        // also defends against any UI side-effect during the SelectedModel write.
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ModelConstants.Flux.Pro11);

        _viewModel.LoadSavedUiState();

        _mockUiStateStore.Verify(s => s.PersistModel(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LoadSavedUiState_IsIdempotent_WhenCalledTwice()
    {
        _mockUiStateStore.Setup(s => s.LoadPrompt()).Returns("restored prompt");
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ModelConstants.Flux.Pro11);

        _viewModel.LoadSavedUiState();
        _viewModel.LoadSavedUiState();

        _mockUiStateStore.Verify(s => s.LoadPrompt(), Times.Once);
        _mockUiStateStore.Verify(s => s.LoadModel(), Times.Once);
    }

    [Fact]
    public void Model_UserPickAfterRestore_StillPersists()
    {
        // The suppression flag is scoped tightly: a real user pick after boot must still
        // persist. This is the positive case that complements the two negative cases above.
        _viewModel.Parameters.Model = ModelConstants.Flux.Pro11;

        _mockUiStateStore.Verify(s => s.PersistModel(ModelConstants.Flux.Pro11), Times.Once);
    }

    // Repro for the bug "model isn't persisted, always shows flux-2-max" — simulate the full
    // OnAppearing sequence (catalog hydrate → restore from store) and verify the user's last-
    // picked model survives a "restart" (a fresh VM instance reading the same store).
    [Fact]
    public async Task ModelPersistAndRestore_AcrossSimulatedRestart_PicksRestoredValue()
    {
        // Stand-in shared store: behaves like a real Preferences-backed singleton, so the
        // second VM "restart" sees the value the first VM wrote.
        string? sharedPrompt = null;
        string? sharedModel = null;
        var sharedStore = new Mock<IUiStateStore>();
        sharedStore.Setup(s => s.PersistPrompt(It.IsAny<string>())).Callback<string>(v => sharedPrompt = v);
        sharedStore.Setup(s => s.PersistModel(It.IsAny<string>())).Callback<string>(v => sharedModel = v);
        sharedStore.Setup(s => s.LoadPrompt()).Returns(() => sharedPrompt);
        sharedStore.Setup(s => s.LoadModel()).Returns(() => sharedModel);

        // Realistic merged catalog (live entries with raw Display, mirrors a real cache hydrate).
        var merged = new List<ModelOption>
        {
            new("flux-1.1-pro", ModelConstants.Flux.Pro11, "Replicate"),
            new("flux-2-max", ModelConstants.Flux.Max2, "Replicate"),
            new("gpt-image-1.5", ModelConstants.OpenAI.GptImage15OnReplicate, "Replicate"),
            new("gpt-image-2", ModelConstants.OpenAI.GptImage2OnReplicate, "Replicate"),
        };
        var coordinator1 = new Mock<IModelCatalogCoordinator>();
        coordinator1.Setup(c => c.LoadCachedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(merged);

        // Session 1: hydrate catalog, user picks gpt-image-2.
        var vm1 = new GeneratorViewModel(
            new Mock<IJobRunner>().Object,
            new Mock<IApiTokenStore>().Object,
            new Mock<IPollinationsTokenStore>().Object,
            new Mock<IComfyUiAuthStore>().Object,
            new Mock<ICivitaiTokenStore>().Object,
            new Mock<IAnthropicTokenStore>().Object,
            new Mock<ICivitaiPostingService>().Object,
            sharedStore.Object,
            coordinator1.Object,
            ModelDescriptorRegistry.Default(),
            new Mock<IPromptBatchParser>().Object,
            new Mock<IComfyUiCheckpointService>().Object,
            new Mock<IGalleryService>().Object,
            new Mock<IFolderPicker>().Object,
            NullLogger<GeneratorViewModel>.Instance);

        await vm1.LoadCachedCatalogAsync();
        vm1.LoadSavedUiState();  // first launch: nothing stored, no-op

        vm1.ProviderFilter.SelectedModel = vm1.ProviderFilter.FilteredModels.First(m => m.Value == ModelConstants.OpenAI.GptImage2OnReplicate);

        sharedModel.Should().Be(ModelConstants.OpenAI.GptImage2OnReplicate,
            "the user's pick must be persisted exactly, not overwritten by a downstream auto-selection");

        // Session 2: fresh VM (simulates app restart). Same store, same catalog.
        var coordinator2 = new Mock<IModelCatalogCoordinator>();
        coordinator2.Setup(c => c.LoadCachedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(merged);

        var vm2 = new GeneratorViewModel(
            new Mock<IJobRunner>().Object,
            new Mock<IApiTokenStore>().Object,
            new Mock<IPollinationsTokenStore>().Object,
            new Mock<IComfyUiAuthStore>().Object,
            new Mock<ICivitaiTokenStore>().Object,
            new Mock<IAnthropicTokenStore>().Object,
            new Mock<ICivitaiPostingService>().Object,
            sharedStore.Object,
            coordinator2.Object,
            ModelDescriptorRegistry.Default(),
            new Mock<IPromptBatchParser>().Object,
            new Mock<IComfyUiCheckpointService>().Object,
            new Mock<IGalleryService>().Object,
            new Mock<IFolderPicker>().Object,
            NullLogger<GeneratorViewModel>.Instance);

        await vm2.LoadCachedCatalogAsync();
        vm2.LoadSavedUiState();

        vm2.Parameters.Model.Should().Be(ModelConstants.OpenAI.GptImage2OnReplicate);
        vm2.ProviderFilter.SelectedModel?.Value.Should().Be(ModelConstants.OpenAI.GptImage2OnReplicate);
        sharedModel.Should().Be(ModelConstants.OpenAI.GptImage2OnReplicate,
            "the stored value must still be the user's pick after the simulated restart");
    }

    // --- Ideogram V4 ---

    private static ModelOption IdeogramOption(GeneratorViewModel vm) =>
        vm.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Ideogram.V4Quality);

    [Fact]
    public void SelectIdeogram_ShowsIdeogramOptions_AndResolutionPicker()
    {
        _viewModel.ProviderFilter.SelectedModel = IdeogramOption(_viewModel);

        _viewModel.SupportsIdeogramOptions.Should().BeTrue();
        _viewModel.SupportsResolution.Should().BeTrue("the single shared picker is visible for Ideogram too");
    }

    [Fact]
    public void SelectIdeogram_ForcesOutputFormatToPng()
    {
        _viewModel.Parameters.OutputFormat = ImageOutputFormat.Webp;

        _viewModel.ProviderFilter.SelectedModel = IdeogramOption(_viewModel);

        _viewModel.Parameters.OutputFormat.Should().Be(ImageOutputFormat.Png);
    }

    [Fact]
    public void LeavingIdeogram_ClearsUseJsonPrompt()
    {
        _viewModel.ProviderFilter.SelectedModel = IdeogramOption(_viewModel);
        _viewModel.Parameters.UseJsonPrompt = true;

        _viewModel.ProviderFilter.SelectedModel =
            _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);

        _viewModel.Parameters.UseJsonPrompt.Should().BeFalse();
    }

    [Fact]
    public void JsonPromptMode_GatesValidityOnValidJson()
    {
        _viewModel.Parameters.ApiToken = "token";
        _viewModel.ProviderFilter.SelectedModel = IdeogramOption(_viewModel);
        _viewModel.Parameters.UseJsonPrompt = true;

        _viewModel.Parameters.Prompt = "not json";
        _viewModel.IsValid.Should().BeFalse("structured-JSON mode requires the box to be valid JSON");

        _viewModel.Parameters.Prompt = """{"high_level_description":"a cat"}""";
        _viewModel.IsValid.Should().BeTrue();
    }

    // --- Batch (textfile) ---

    [Fact]
    public async Task RunBatchAsync_PreservesFileOrder_TopJobIsFirstPrompt()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await _viewModel.Batch.RunBatchAsync(new[] { "P1", "P2", "P3" });

        _viewModel.Jobs.Should().HaveCount(3);
        _viewModel.Jobs[0].Prompt.Should().Be("P1");
        _viewModel.Jobs[1].Prompt.Should().Be("P2");
        _viewModel.Jobs[2].Prompt.Should().Be("P3");
        _viewModel.Jobs.Should().AllSatisfy(j =>
        {
            j.StatusKind.Should().Be(StatusKind.Success);
            j.IsRunning.Should().BeFalse();
        });
        _viewModel.Batch.IsBatchRunning.Should().BeFalse();
        _viewModel.StatusMessage.Should().Contain("3 ok").And.Contain("0 failed");
    }

    [Fact]
    public async Task RunBatchAsync_OneJobFails_OthersStillRunAndSummaryFlagsFailure()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        var call = 0;
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref call);
                // Middle prompt fails; others succeed.
                return call == 2
                    ? new JobOutcome(JobOutcomeKind.Failed, null, "boom")
                    : new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}");
            });

        await _viewModel.Batch.RunBatchAsync(new[] { "A", "B", "C" });

        _viewModel.Jobs.Should().HaveCount(3);
        _viewModel.Jobs[0].StatusKind.Should().Be(StatusKind.Success); // A
        _viewModel.Jobs[1].StatusKind.Should().Be(StatusKind.Error);   // B
        _viewModel.Jobs[2].StatusKind.Should().Be(StatusKind.Success); // C
        _viewModel.StatusMessage.Should().Contain("2 ok").And.Contain("1 failed");
        _viewModel.StatusKind.Should().Be(StatusKind.Warning);
    }

    [Fact]
    public async Task RunBatchAsync_RandomizeSeed_GivesEachJobADistinctSeed()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        // Pick a model that supports seed so RandomizeSeed actually matters.
        _viewModel.ProviderFilter.SelectedModel = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);
        _viewModel.Parameters.RandomizeSeed = true;

        var capturedSeeds = new List<long>();
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync((ImageGenerationParameters p, CancellationToken _, IProgress<JobProgress>? _) =>
            {
                capturedSeeds.Add(p.Seed);
                return new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, "ok");
            });

        await _viewModel.Batch.RunBatchAsync(new[] { "p1", "p2", "p3" });

        capturedSeeds.Should().HaveCount(3);
        capturedSeeds.Distinct().Should().HaveCount(3,
            "each prompt should get its own randomized seed when RandomizeSeed is true");
    }

    [Fact]
    public async Task RunBatchAsync_EmptyList_NoJobsAddedAndIsBatchRunningStaysFalse()
    {
        _viewModel.Parameters.ApiToken = "valid-token";

        await _viewModel.Batch.RunBatchAsync(Array.Empty<string>());

        _viewModel.Jobs.Should().BeEmpty();
        _viewModel.Batch.IsBatchRunning.Should().BeFalse();
    }

    [Fact]
    public async Task CancelBatch_LetsInFlightJobFinish_AndDrainsQueue()
    {
        // Replicate predictions are already paid for once they're submitted — the user's
        // explicit constraint is that CancelBatch must not abort the in-flight job, only
        // stop the queue from starting any further jobs.
        _viewModel.Parameters.ApiToken = "valid-token";

        var firstJobGate = new TaskCompletionSource<JobOutcome>();
        var firstJobStarted = new TaskCompletionSource<bool>();
        var runAsyncCalls = 0;
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .Returns((ImageGenerationParameters _, CancellationToken __, IProgress<JobProgress>? ___) =>
            {
                if (Interlocked.Increment(ref runAsyncCalls) == 1)
                {
                    firstJobStarted.TrySetResult(true);
                    return firstJobGate.Task;
                }
                // If the loop ever reaches a second call, the test fails — the queue
                // should have been drained without invoking the runner again.
                throw new InvalidOperationException("Runner should not be called for canceled-queued jobs.");
            });

        var batchTask = _viewModel.Batch.RunBatchAsync(new[] { "first", "second", "third" });

        await firstJobStarted.Task; // wait until the first job is actually running
        _viewModel.Batch.CancelBatchCommand.Execute(null);

        // Let the in-flight job finish naturally (success).
        firstJobGate.SetResult(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));
        await batchTask;

        _viewModel.Jobs.Should().HaveCount(3);
        _viewModel.Jobs[0].StatusKind.Should().Be(StatusKind.Success, "the in-flight job should finish, not be canceled");
        _viewModel.Jobs[1].StatusKind.Should().Be(StatusKind.Canceled);
        _viewModel.Jobs[2].StatusKind.Should().Be(StatusKind.Canceled);
        runAsyncCalls.Should().Be(1, "queued jobs after the cancel must not be submitted to the runner");
        _viewModel.StatusMessage.Should().Contain("1 ok").And.Contain("2 canceled");
    }

    // --- Structured-JSON validation chain (paste/clear/paste regression + state label) ------

    private void SelectIdeogramTurbo()
    {
        var ideogram = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Ideogram.V4Turbo);
        _viewModel.ProviderFilter.SelectedModel = ideogram;
        _viewModel.Parameters.ApiToken = "valid-token";
    }

    // Pins the VM half of the paste -> clear -> paste-new repro: every Prompt write must
    // revalidate. (The in-app bug was the WinUI Editor binding not delivering the writes;
    // the view fix splits the binding, this proves the VM does its part for each delivery.)
    [Fact]
    public void IsValid_JsonMode_TracksEveryPromptRewrite()
    {
        SelectIdeogramTurbo();
        _viewModel.Parameters.UseJsonPrompt = true;

        _viewModel.Parameters.Prompt = "{\"high_level_description\":\"x\"}";
        _viewModel.IsValid.Should().BeTrue();

        _viewModel.Parameters.Prompt = string.Empty;
        _viewModel.IsValid.Should().BeFalse();

        _viewModel.Parameters.Prompt = "definitely not json";
        _viewModel.IsValid.Should().BeFalse();

        _viewModel.Parameters.Prompt = "{\"high_level_description\":\"y\"}";
        _viewModel.IsValid.Should().BeTrue();
    }

    [Fact]
    public void JsonPromptStateText_NullOutsideStructuredMode()
    {
        SelectIdeogramTurbo();
        _viewModel.Parameters.Prompt = "anything";

        _viewModel.JsonPromptStateText.Should().BeNull("checkbox is off");

        _viewModel.Parameters.UseJsonPrompt = true;
        _viewModel.JsonPromptStateText.Should().NotBeNull();

        _viewModel.Parameters.UseJsonPrompt = false;
        _viewModel.JsonPromptStateText.Should().BeNull("toggling off must clear the label");
    }

    [Fact]
    public void JsonPromptStateText_NullForNonIdeogramModels()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "plain prompt"; // default model is GPT, not Ideogram

        _viewModel.JsonPromptStateText.Should().BeNull();
    }

    [Fact]
    public void LoadSavedUiState_RestoresUseJsonPrompt_OnSavedIdeogramModel()
    {
        _mockUiStateStore.Setup(s => s.LoadPrompt()).Returns("{\"a\":1}");
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ModelConstants.Ideogram.V4Turbo);
        _mockUiStateStore.Setup(s => s.LoadUseJsonPrompt()).Returns(true);

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.UseJsonPrompt.Should().BeTrue();
        // The restore itself must not re-persist (it isn't a user preference change).
        _mockUiStateStore.Verify(s => s.PersistUseJsonPrompt(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void LoadSavedUiState_IgnoresSavedToggle_OnNonIdeogramModel()
    {
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ModelConstants.Flux.Pro11);
        _mockUiStateStore.Setup(s => s.LoadUseJsonPrompt()).Returns(true);

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.UseJsonPrompt.Should().BeFalse("the toggle is meaningless on non-Ideogram models");
    }

    [Fact]
    public void UseJsonPrompt_UserToggle_Persists()
    {
        SelectIdeogramTurbo();

        _viewModel.Parameters.UseJsonPrompt = true;

        _mockUiStateStore.Verify(s => s.PersistUseJsonPrompt(true), Times.Once);
    }

    [Fact]
    public void UseJsonPrompt_AutoClearOnModelSwitch_DoesNotPersistFalse()
    {
        SelectIdeogramTurbo();
        _viewModel.Parameters.UseJsonPrompt = true;

        var flux = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);
        _viewModel.ProviderFilter.SelectedModel = flux;

        _viewModel.Parameters.UseJsonPrompt.Should().BeFalse("RefreshCapabilities auto-clears on non-Ideogram models");
        _mockUiStateStore.Verify(s => s.PersistUseJsonPrompt(false), Times.Never,
            "a capability-driven clear must not erase the user's saved preference");
    }

    private const string ComfyModelId = "comfyui/Ideogram workflow_MR";

    private void SelectComfyWorkflow()
    {
        // Workflow entries come from the folder-scan catalog, not the seed list — surface one
        // (next to the models the switch tests hop between) the way ApplyCatalog would after
        // a real load. ApplyCatalog replaces AllModels, so include everything needed.
        _viewModel.ProviderFilter.ApplyCatalog(
        [
            new ModelOption("Ideogram V4 Turbo", ModelConstants.Ideogram.V4Turbo, "Replicate"),
            new ModelOption("Flux 1.1 Pro", ModelConstants.Flux.Pro11, "Replicate"),
            new ModelOption("Ideogram workflow_MR (ComfyUI)", ComfyModelId, "ComfyUI"),
        ]);
        _viewModel.ProviderFilter.SelectedModel =
            _viewModel.ProviderFilter.AllModels.First(m => m.Value == ComfyModelId);
    }

    [Fact]
    public void UseJsonPrompt_SurvivesIdeogramToComfySwitch_ButClearsOnFlux()
    {
        SelectComfyWorkflow();
        SelectIdeogramTurbo();
        _viewModel.Parameters.UseJsonPrompt = true;

        _viewModel.ProviderFilter.SelectedModel =
            _viewModel.ProviderFilter.AllModels.First(m => m.Value == ComfyModelId);
        _viewModel.Parameters.UseJsonPrompt.Should().BeTrue(
            "ComfyUI workflow models consume the caption JSON, so the toggle must survive");

        var flux = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);
        _viewModel.ProviderFilter.SelectedModel = flux;
        _viewModel.Parameters.UseJsonPrompt.Should().BeFalse("Flux has no JSON prompt field");
    }

    [Fact]
    public void IsValid_ComfyModel_RequiresNoApiToken_ButValidatesJsonMode()
    {
        SelectComfyWorkflow();
        _viewModel.Parameters.ApiToken = "";
        _viewModel.Parameters.Prompt = "a plain prompt";

        _viewModel.IsValid.Should().BeTrue("the ComfyUI server is the user's own — no token exists");

        _viewModel.Parameters.UseJsonPrompt = true;
        _viewModel.IsValid.Should().BeFalse("JSON mode demands valid JSON");
        _viewModel.JsonPromptStateText.Should().Contain("not valid");

        _viewModel.Parameters.Prompt = "{\"high_level_description\":\"x\"}";
        _viewModel.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateImage_ComfyModel_RunsWithoutApiToken()
    {
        SelectComfyWorkflow();
        _viewModel.Parameters.ApiToken = "";
        _viewModel.Parameters.Prompt = "a plain prompt";

        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.Jobs.Should().HaveCount(1, "the missing-token gate must exempt comfyui/* models");
        _viewModel.Jobs[0].StatusKind.Should().Be(StatusKind.Success);
    }

    [Fact]
    public void Resolution_UserPick_Persists()
    {
        SelectIdeogramTurbo(); // RefreshCapabilities defaults Resolution to "Auto" (suppressed)

        _viewModel.Parameters.Resolution = "1440x2880";

        _mockUiStateStore.Verify(s => s.PersistResolution("1440x2880", It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void Resolution_CapabilityFallbackOnModelSwitch_DoesNotPersist()
    {
        SelectIdeogramTurbo(); // forces Resolution -> "Auto" because the prior value isn't Ideogram-valid

        _viewModel.Parameters.Resolution.Should().Be("Auto");
        _mockUiStateStore.Verify(s => s.PersistResolution(It.IsAny<string>(), It.IsAny<string?>()), Times.Never,
            "a capability fallback isn't a user pick and must not overwrite the saved choice");
    }

    [Fact]
    public void LoadSavedUiState_RestoresResolution_WhenSavedModelOffersIt()
    {
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ModelConstants.Ideogram.V4Turbo);
        _mockUiStateStore.Setup(s => s.LoadResolution(It.IsAny<string?>())).Returns("1440x2880");

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.Resolution.Should().Be("1440x2880");
        // The restore itself must not re-persist.
        _mockUiStateStore.Verify(s => s.PersistResolution(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void LoadSavedUiState_IgnoresResolution_NotOfferedByTheModel()
    {
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ModelConstants.Ideogram.V4Turbo);
        _mockUiStateStore.Setup(s => s.LoadResolution(It.IsAny<string?>())).Returns("999x999");

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.Resolution.Should().Be("Auto", "the capability fallback stays when the saved value is foreign");
    }

    [Fact]
    public void JsonPromptStateText_ReflectsJsonValidity()
    {
        SelectIdeogramTurbo();
        _viewModel.Parameters.UseJsonPrompt = true;

        _viewModel.Parameters.Prompt = "{\"a\":1}";
        _viewModel.JsonPromptStateText.Should().Contain("valid ✓");

        _viewModel.Parameters.Prompt = "not json";
        _viewModel.JsonPromptStateText.Should().Contain("not valid");
    }

    [Fact]
    public void JsonPromptStateText_InvalidJson_ShowsParseDetailWithPosition()
    {
        // "not valid JSON" alone sent the user hunting through a 1700-char compact blob for
        // a missing root brace (2026-06-11); the label must say what broke and where.
        SelectIdeogramTurbo();
        _viewModel.Parameters.UseJsonPrompt = true;

        _viewModel.Parameters.Prompt = "{\"a\":1"; // unclosed root object

        _viewModel.JsonPromptStateText.Should().Contain("not valid");
        _viewModel.JsonPromptStateText.Should().Contain("pos", "the parse-error position guides the fix");
    }

    // The four tests below pin the view-layer interaction that broke resolution persistence
    // in-app while every pure-VM test stayed green: the MainPage Picker two-way binds
    // SelectedItem to Parameters.Resolution, and replacing its ItemsSource (the
    // ResolutionOptions swap in RefreshCapabilities) makes it push null — synchronously,
    // from inside the ResolutionOptions PropertyChanged cascade. Unsuppressed, that push
    // persisted null and deleted the saved key before LoadSavedUiState could read it.

    [Fact]
    public void Resolution_PickerNullPushDuringOptionsSwap_DoesNotPersist_AndFallbackRecovers()
    {
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneratorViewModel.ResolutionOptions))
                _viewModel.Parameters.Resolution = null!;
        };

        SelectIdeogramTurbo();

        _mockUiStateStore.Verify(s => s.PersistResolution(It.IsAny<string>(), It.IsAny<string?>()), Times.Never,
            "a binding artifact must never overwrite the saved resolution");
        _viewModel.Parameters.Resolution.Should().Be("Auto", "the capability fallback recovers from the null push");
    }

    [Fact]
    public void Resolution_PickerFirstItemPushDuringOptionsSwap_DoesNotPersist()
    {
        // Variant: a Picker that resets to the first row instead of null pushes a value that
        // IS in the new list — only the widened suppression window can tell it from a user pick.
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneratorViewModel.ResolutionOptions) && _viewModel.ResolutionOptions.Count > 0)
                _viewModel.Parameters.Resolution = _viewModel.ResolutionOptions[0];
        };

        SelectIdeogramTurbo();

        _mockUiStateStore.Verify(s => s.PersistResolution(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Resolution_WriteOutsideCurrentOptions_DoesNotPersist()
    {
        SelectIdeogramTurbo();

        // E.g. a Picker pushing null on binding attach (page recreation), or any stale write
        // the current model doesn't offer.
        _viewModel.Parameters.Resolution = null!;
        _viewModel.Parameters.Resolution = "999x999";

        _mockUiStateStore.Verify(s => s.PersistResolution(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void LoadSavedUiState_RestoresResolution_DespitePickerNullPushOnOptionsSwap()
    {
        // End-to-end repro of the in-app startup: the model restore swaps ResolutionOptions,
        // the bound Pickers push null mid-swap, and the saved value must still both survive
        // in storage and win in Parameters.
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ModelConstants.Ideogram.V4Turbo);
        _mockUiStateStore.Setup(s => s.LoadResolution(It.IsAny<string?>())).Returns("1440x2880");
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneratorViewModel.ResolutionOptions))
                _viewModel.Parameters.Resolution = null!;
        };

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.Resolution.Should().Be("1440x2880");
        _mockUiStateStore.Verify(s => s.PersistResolution(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void LoadSavedUiState_RestoresAspectRatio_DespitePickerPushOnOptionsSwap()
    {
        // AR analog of the resolution test above, but the AR Picker pushes a VALID first/default
        // option on the ItemsSource swap (not null). Unsuppressed, that push persisted the default
        // and clobbered the user's saved AR before LoadSavedUiState restored it. The
        // _suppressAspectRatioPersist window around the RefreshCapabilities swap must prevent that.
        SelectComfyWorkflow(); // catalog contains the workflow entry (its AR list has "2:3 (Portrait Photo)")
        SelectIdeogramTurbo(); // move away so the restore below actually switches back and swaps the AR options
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ComfyModelId);
        _mockUiStateStore.Setup(s => s.LoadAspectRatio(It.IsAny<string?>())).Returns("2:3 (Portrait Photo)");
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneratorViewModel.AspectRatioOptions)
                && _viewModel.AspectRatioOptions.Count > 0)
                _viewModel.Parameters.AspectRatio = _viewModel.AspectRatioOptions[0]; // "1:1 (Square)"
        };

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.AspectRatio.Should().Be("2:3 (Portrait Photo)");
        _mockUiStateStore.Verify(
            s => s.PersistAspectRatio(It.IsAny<string>(), It.IsAny<string?>()), Times.Never,
            "a binding artifact must never overwrite the saved aspect ratio");
    }

    [Fact]
    public void Resolution_SurvivesIdeogramToIdeogramModelSwitch_DespitePickerNullPush()
    {
        // Round 2 of the restart bug: the options swap synchronously nulls the value via the
        // bound Pickers BEFORE the old code's membership check ran, so a still-valid choice
        // was slammed to the first option on every model switch. The capture-before-swap fix
        // must carry the user's pick across an Ideogram->Ideogram switch (same options list).
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneratorViewModel.ResolutionOptions))
                _viewModel.Parameters.Resolution = null!;
        };
        SelectIdeogramTurbo();
        _viewModel.Parameters.Resolution = "1440x2880"; // user pick, persists once

        var quality = _viewModel.ProviderFilter.AllModels.First(m => m.Value == ModelConstants.Ideogram.V4Quality);
        _viewModel.ProviderFilter.SelectedModel = quality;

        _viewModel.Parameters.Resolution.Should().Be("1440x2880", "the new model offers the same value");
        _mockUiStateStore.Verify(s => s.PersistResolution("1440x2880", It.IsAny<string?>()), Times.Once,
            "only the user pick persists; the switch must neither re-persist nor overwrite");
    }

    [Fact]
    public void Resolution_SavedValueAdopted_WhenSwitchingToModelThatOffersIt()
    {
        // Mid-session sticky restore: coming from a model without resolutions, the saved
        // user preference (not the first option) wins when the new model offers it.
        _mockUiStateStore.Setup(s => s.LoadResolution(It.IsAny<string?>())).Returns("1440x2880");

        SelectIdeogramTurbo();

        _viewModel.Parameters.Resolution.Should().Be("1440x2880");
        _mockUiStateStore.Verify(s => s.PersistResolution(It.IsAny<string>(), It.IsAny<string?>()), Times.Never,
            "adopting the saved value is a restore, not a user pick");
    }

    // Resolution persistence is per option-format FAMILY (ComfyUI MP presets vs everything
    // else): the store keys on the model id so Ideogram <-> ComfyUI switches restore each
    // family's last pick instead of slamming to the first option.

    [Fact]
    public void Resolution_ComfyPick_PersistsUnderTheComfyModelsFamily()
    {
        SelectComfyWorkflow();

        _viewModel.Parameters.Resolution = "2.0 MP";

        _mockUiStateStore.Verify(s => s.PersistResolution("2.0 MP", ComfyModelId), Times.Once);
    }

    [Fact]
    public void RefreshCapabilities_LoadsSavedResolution_ForTheNewModelsFamily()
    {
        _mockUiStateStore.Setup(s => s.LoadResolution(ComfyModelId)).Returns("2.0 MP");

        SelectComfyWorkflow();

        _viewModel.Parameters.Resolution.Should().Be("2.0 MP",
            "the switch must consult the NEW model's family, not the legacy key");
    }

    [Fact]
    public void Resolution_SwitchIdeogramToComfyAndBack_RestoresEachFamilysChoice()
    {
        _mockUiStateStore
            .Setup(s => s.LoadResolution(It.Is<string?>(m => ModelConstants.ComfyUi.IsId(m))))
            .Returns("2.0 MP");
        _mockUiStateStore
            .Setup(s => s.LoadResolution(It.Is<string?>(m => !ModelConstants.ComfyUi.IsId(m))))
            .Returns("1440x2880");

        SelectComfyWorkflow();
        SelectIdeogramTurbo();
        _viewModel.Parameters.Resolution.Should().Be("1440x2880");

        _viewModel.ProviderFilter.SelectedModel =
            _viewModel.ProviderFilter.AllModels.First(m => m.Value == ComfyModelId);
        _viewModel.Parameters.Resolution.Should().Be("2.0 MP");

        SelectIdeogramTurbo();
        _viewModel.Parameters.Resolution.Should().Be("1440x2880");
    }

    [Fact]
    public void LoadSavedUiState_RestoresComfyResolution_WhenSavedModelIsComfy()
    {
        SelectComfyWorkflow(); // catalog contains the workflow entry
        SelectIdeogramTurbo(); // move away so the restore below actually switches back
        _mockUiStateStore.Setup(s => s.LoadModel()).Returns(ComfyModelId);
        _mockUiStateStore.Setup(s => s.LoadResolution(ComfyModelId)).Returns("2.0 MP");

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.Model.Should().Be(ComfyModelId);
        _viewModel.Parameters.Resolution.Should().Be("2.0 MP");
    }

    [Fact]
    public void Resolution_DeferredNullPushOutsideSuppression_IsReverted()
    {
        // In-app, the WinUI ComboBox finishes its ItemsSource rebuild on a LATER dispatcher
        // tick than the ResolutionOptions swap and pushes SelectedItem=null then — outside
        // the RefreshCapabilities suppression window, where no flag can identify it. This is
        // what wiped the resolution on every mid-session model switch (round 2 of the bug).
        SelectIdeogramTurbo();
        _viewModel.Parameters.Resolution = "1440x2880"; // user pick

        _viewModel.Parameters.Resolution = null!; // the deferred platform push

        _viewModel.Parameters.Resolution.Should().Be("1440x2880",
            "an unsuppressed null while the model offers options is a binding artifact, never a user pick");
        _mockUiStateStore.Verify(s => s.PersistResolution("1440x2880", It.IsAny<string?>()), Times.Once,
            "the revert is a restore and must not double-persist");
    }

    [Fact]
    public void Resolution_ComfyDeferredNullPush_IsReverted()
    {
        // Same deferred-null-push guard as above, pinned for the ComfyUI megapixel family —
        // the 2026-06-11 livelock froze the app exactly on this value family, and the single
        // surviving picker relies on this revert after every ItemsSource swap.
        SelectComfyWorkflow();
        _viewModel.Parameters.Resolution = "4.0 MP"; // user pick

        _viewModel.Parameters.Resolution = null!; // the deferred platform push

        _viewModel.Parameters.Resolution.Should().Be("4.0 MP",
            "an unsuppressed null while the model offers options is a binding artifact, never a user pick");
        _mockUiStateStore.Verify(s => s.PersistResolution("4.0 MP", It.IsAny<string?>()), Times.Once,
            "the revert is a restore and must not double-persist");
    }

    [Fact]
    public void EditorResolutionPick_WritesThroughToGenerator_AndPersists()
    {
        // The editor hands the resolution back via ApplyToGenerator only on Apply; a
        // pick must ALSO reach the generator immediately so leaving via back navigation
        // doesn't discard it.
        SelectIdeogramTurbo();
        var editor = new IdeogramStructureEditorViewModel(
            Mock.Of<IJsonPromptFileService>(),
            Mock.Of<IFileLauncher>(),
            NullLogger<IdeogramStructureEditorViewModel>.Instance,
            _viewModel);

        editor.SelectedResolution = "1440x2880";

        _viewModel.Parameters.Resolution.Should().Be("1440x2880");
        _mockUiStateStore.Verify(s => s.PersistResolution("1440x2880", It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void EditorSeeding_DoesNotWriteBackToGenerator()
    {
        // Seeding reflects the generator's state; its Auto fallback for unknown values must
        // not clobber the generator's choice just because the editor opened.
        SelectIdeogramTurbo();
        _viewModel.Parameters.Resolution = "1440x2880";
        var editor = new IdeogramStructureEditorViewModel(
            Mock.Of<IJsonPromptFileService>(),
            Mock.Of<IFileLauncher>(),
            NullLogger<IdeogramStructureEditorViewModel>.Instance,
            _viewModel);

        editor.SetIncomingResolution("999x999");

        editor.SelectedResolution.Should().Be("Auto");
        _viewModel.Parameters.Resolution.Should().Be("1440x2880");
        _mockUiStateStore.Verify(s => s.PersistResolution("1440x2880", It.IsAny<string?>()), Times.Once,
            "only the user's original pick persists");
    }

    [Fact]
    public void EditorApply_WritesJsonPromptAndResolution_DirectlyToGenerator()
    {
        // Direct hand-off, NOT a //MainPage?ideogramJson=… route: Shell re-applies string
        // query parameters on every back navigation, which used to resurrect the stale JSON
        // over a freshly typed prompt whenever the user backed out of the editor.
        SelectIdeogramTurbo();
        var editor = CreateEditor();
        editor.SelectedResolution = "1440x2880";
        _viewModel.Parameters.Resolution = "Auto"; // changed behind the editor's back

        editor.ApplyToGenerator("{\"a\":1}");

        _viewModel.Parameters.Prompt.Should().Be("{\"a\":1}");
        _viewModel.Parameters.UseJsonPrompt.Should().BeTrue();
        _viewModel.Parameters.Resolution.Should().Be("1440x2880", "Apply re-adopts the editor's pick");
    }

    [Fact]
    public void EditorApply_ArMode_LeavesGeneratorResolutionAlone()
    {
        // In AR mode SelectedResolution is an aspect-ratio combo string that isn't in the
        // generator's ResolutionOptions — the membership guard must drop it (the live
        // write-through already delivered the pick via Parameters.AspectRatio).
        SelectComfyWorkflow();
        var editor = CreateEditor();
        var resolutionBefore = _viewModel.Parameters.Resolution;

        editor.ApplyToGenerator("{\"a\":1}");

        _viewModel.Parameters.Prompt.Should().Be("{\"a\":1}");
        _viewModel.Parameters.UseJsonPrompt.Should().BeTrue();
        _viewModel.Parameters.Resolution.Should().Be(resolutionBefore);
    }

    // --- Editor AR mode (host model = ComfyUI workflow) -----------------------------------
    // On comfyui/* the output shape is the ResolutionSelector's aspect-ratio combo string,
    // not an Ideogram "WxH" resolution: the editor's picker mirrors the generator's AR
    // options and writes through to Parameters.AspectRatio.

    private IdeogramStructureEditorViewModel CreateEditor() => new(
        Mock.Of<IJsonPromptFileService>(),
        Mock.Of<IFileLauncher>(),
        NullLogger<IdeogramStructureEditorViewModel>.Instance,
        _viewModel);

    [Fact]
    public void Editor_ComfyModel_ListsGeneratorAspectRatioOptions_AndAspectRatioTitle()
    {
        SelectComfyWorkflow();

        var editor = CreateEditor();

        editor.IsAspectRatioMode.Should().BeTrue();
        editor.ResolutionPickerTitle.Should().Be("Aspect ratio");
        editor.ResolutionOptions.Should().BeEquivalentTo(_viewModel.AspectRatioOptions);
    }

    [Fact]
    public void Editor_IdeogramModel_StaysInResolutionMode()
    {
        SelectIdeogramTurbo();

        var editor = CreateEditor();

        editor.IsAspectRatioMode.Should().BeFalse();
        editor.ResolutionPickerTitle.Should().Be("Target resolution");
        editor.ResolutionOptions.Should().Contain("1440x2880");
    }

    [Fact]
    public void Editor_ComfyModel_SeedsSelectionFromIncomingAspectRatio_WithoutWritingBack()
    {
        SelectComfyWorkflow();
        _viewModel.Parameters.AspectRatio = "3:4 (Portrait Standard)";
        var editor = CreateEditor();

        editor.SetIncomingResolution("3:4 (Portrait Standard)");

        editor.SelectedResolution.Should().Be("3:4 (Portrait Standard)");
        _viewModel.Parameters.AspectRatio.Should().Be("3:4 (Portrait Standard)");
    }

    [Fact]
    public void Editor_ComfyModel_UnknownIncomingValue_FallsBackToFirstAspectRatio()
    {
        SelectComfyWorkflow();
        var editor = CreateEditor();

        editor.SetIncomingResolution("1440x2880"); // an Ideogram resolution is foreign here

        editor.SelectedResolution.Should().Be(_viewModel.AspectRatioOptions[0]);
    }

    [Fact]
    public void Editor_ComfyModel_AspectRatioPick_WritesThroughToGeneratorAspectRatio()
    {
        SelectComfyWorkflow();
        var editor = CreateEditor();

        editor.SelectedResolution = "9:16 (Portrait Widescreen)";

        _viewModel.Parameters.AspectRatio.Should().Be("9:16 (Portrait Widescreen)");
    }

    [Fact]
    public void Editor_ComfyModel_AspectRatioPick_NeverTouchesGeneratorResolution()
    {
        SelectComfyWorkflow();
        _viewModel.Parameters.Resolution = "2.0 MP"; // user's MP pick must survive
        var editor = CreateEditor();

        editor.SelectedResolution = "9:16 (Portrait Widescreen)";

        _viewModel.Parameters.Resolution.Should().Be("2.0 MP");
    }

    [Fact]
    public void Editor_ComfyModel_CanvasShapeFollowsAspectRatioLabel()
    {
        SelectComfyWorkflow();
        var editor = CreateEditor();

        editor.SelectedResolution = "3:4 (Portrait Standard)";

        editor.CanvasHeightRequest.Should().Be(IdeogramStructureEditorViewModel.CanvasFitBox);
        editor.CanvasWidthRequest.Should().BeApproximately(
            IdeogramStructureEditorViewModel.CanvasFitBox * 3 / 4, 0.001);
    }

    [Fact]
    public void BuildEditorRoute_ComfyModel_CarriesTheAspectRatio()
    {
        SelectComfyWorkflow();
        _viewModel.Parameters.AspectRatio = "3:4 (Portrait Standard)";

        _viewModel.BuildEditorRoute().Should().Contain(
            $"resolution={Uri.EscapeDataString("3:4 (Portrait Standard)")}");
    }

    [Fact]
    public void BuildEditorRoute_IdeogramModel_CarriesTheResolution()
    {
        SelectIdeogramTurbo();
        _viewModel.Parameters.Resolution = "1440x2880";

        _viewModel.BuildEditorRoute().Should().Contain("resolution=1440x2880");
    }

    // --- Live job progress (ComfyUI ws → job card) ------------------------------------------

    [Fact]
    public async Task ProgressReports_UpdateJobCard_AndClearWithTheOutcome()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "a cat";
        IProgress<JobProgress>? sink = null;
        var started = new TaskCompletionSource();
        var gate = new TaskCompletionSource<JobOutcome>();
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .Returns((ImageGenerationParameters _, CancellationToken __, IProgress<JobProgress>? prog) =>
            {
                sink = prog;
                started.TrySetResult();
                return gate.Task;
            });

        var run = ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);
        await started.Task;
        var job = _viewModel.Jobs[0];

        sink.Should().NotBeNull("the VM must hand the runner a progress sink");
        sink!.Report(new JobProgress("Rendering… 5/20", 0.25));

        // Progress<T> marshals through the ThreadPool in tests — wait for delivery.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (job.StatusMessage != "Rendering… 5/20" && DateTime.UtcNow < deadline) await Task.Delay(10);

        job.StatusMessage.Should().Be("Rendering… 5/20");
        job.Progress.Should().Be(0.25);
        job.HasProgress.Should().BeTrue();

        gate.SetResult(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));
        await run;

        job.HasProgress.Should().BeFalse("the bar must disappear with the final outcome");
        job.StatusMessage.Should().Contain("Saved to");
    }

    [Fact]
    public async Task ProgressReport_AfterCompletion_IsIgnored()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "a cat";
        IProgress<JobProgress>? sink = null;
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .Returns((ImageGenerationParameters _, CancellationToken __, IProgress<JobProgress>? prog) =>
            {
                sink = prog;
                return Task.FromResult(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));
            });

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);
        var job = _viewModel.Jobs[0];
        job.IsRunning.Should().BeFalse();

        // A straggler ws frame delivered after the outcome landed must not overwrite it.
        sink!.Report(new JobProgress("Rendering… 19/20", 0.95));
        await Task.Delay(150);

        job.StatusMessage.Should().Contain("Saved to");
        job.HasProgress.Should().BeFalse();
    }

    // --- ComfyUI checkpoint picker ---------------------------------------------------------
    // Options[0] is always the workflow's baked-in checkpoint (= empty Parameters value = no
    // patch); server names follow. Hidden when the workflow has no literal loader. The picker
    // binds the VM-level SelectedCheckpoint, so the null-push tolerance lives in the VM.

    private const string ComfyWorkflowName = "Ideogram workflow_MR";

    private void SetupCheckpointService(
        string? baked, params string[] server) =>
        SetupModelSlotService(
            baked is null ? null : new ComfyUiModelSlot(ComfyUiLoaderKind.Checkpoint, baked), server);

    private void SetupModelSlotService(ComfyUiModelSlot? slot, params string[] server)
    {
        _mockCheckpointService
            .Setup(s => s.GetWorkflowModelSlotAsync(ComfyWorkflowName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(slot);
        _mockCheckpointService
            .Setup(s => s.GetModelNamesAsync(It.IsAny<ComfyUiLoaderKind>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(server.Length == 0 ? null : server.ToList());
    }

    [Fact]
    public async Task ComfyModel_WithBakedCheckpoint_ShowsPickerWithDefaultFirst()
    {
        SetupCheckpointService("baked.safetensors", "a.safetensors", "baked.safetensors", "b.safetensors");

        SelectComfyWorkflow();
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);

        _viewModel.SupportsCheckpoint.Should().BeTrue();
        // The workflow default leads and the server's copy of it is deduplicated.
        _viewModel.CheckpointOptions.Should().Equal("baked.safetensors", "a.safetensors", "b.safetensors");
        _viewModel.SelectedCheckpoint.Should().Be("baked.safetensors");
        _viewModel.CheckpointLabel.Should().Be("Checkpoint");
        _viewModel.Parameters.ComfyUiCheckpoint.Should().BeEmpty("the default selection means no patch");
    }

    [Fact]
    public async Task ComfyModel_SingleUnetWorkflow_ShowsPickerLabeledDiffusionModel()
    {
        SetupModelSlotService(
            new ComfyUiModelSlot(ComfyUiLoaderKind.Unet, "ideogram4_fp8_scaled.safetensors"),
            "flux-dev.safetensors", "ideogram4_fp8_scaled.safetensors");

        SelectComfyWorkflow();
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);

        _viewModel.SupportsCheckpoint.Should().BeTrue();
        _viewModel.CheckpointLabel.Should().Be("Diffusion model");
        _viewModel.CheckpointOptions.Should().Equal("ideogram4_fp8_scaled.safetensors", "flux-dev.safetensors");
        _mockCheckpointService.Verify(
            s => s.GetModelNamesAsync(ComfyUiLoaderKind.Unet, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce, "the server list must come from the workflow's own loader kind");
    }

    [Fact]
    public async Task NonComfyModel_ResetsCheckpointLabel()
    {
        SetupModelSlotService(new ComfyUiModelSlot(ComfyUiLoaderKind.Unet, "ideogram4_fp8_scaled.safetensors"));
        SelectComfyWorkflow();
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);
        _viewModel.CheckpointLabel.Should().Be("Diffusion model");

        SelectIdeogramTurbo();
        await _viewModel.RefreshCheckpointOptionsAsync(ModelConstants.Ideogram.V4Turbo);

        _viewModel.CheckpointLabel.Should().Be("Checkpoint");
    }

    [Fact]
    public async Task ComfyModel_WithoutSwappableLoader_HidesPicker()
    {
        // Covers no-loader, link-only, AND multi-UNET pairings — the slot probe returns null
        // for all of them.
        SetupCheckpointService(baked: null, "a.safetensors");

        SelectComfyWorkflow();
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);

        _viewModel.SupportsCheckpoint.Should().BeFalse();
        _viewModel.CheckpointOptions.Should().BeEmpty();
        _viewModel.Parameters.ComfyUiCheckpoint.Should().BeEmpty();
    }

    [Fact]
    public async Task ComfyModel_ServerUnreachable_ShowsOnlyTheWorkflowDefault()
    {
        SetupCheckpointService("baked.safetensors" /* no server list */);

        SelectComfyWorkflow();
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);

        _viewModel.SupportsCheckpoint.Should().BeTrue();
        _viewModel.CheckpointOptions.Should().Equal("baked.safetensors");
        _viewModel.SelectedCheckpoint.Should().Be("baked.safetensors");
    }

    [Fact]
    public async Task ExplicitCheckpointPick_WritesParametersAndPersists_DefaultPickWritesEmpty()
    {
        SetupCheckpointService("baked.safetensors", "a.safetensors");
        SelectComfyWorkflow();
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);

        _viewModel.SelectedCheckpoint = "a.safetensors";

        _viewModel.Parameters.ComfyUiCheckpoint.Should().Be("a.safetensors");
        _mockUiStateStore.Verify(s => s.PersistComfyUiCheckpoint("a.safetensors", ComfyWorkflowName), Times.Once);

        _viewModel.SelectedCheckpoint = "baked.safetensors";

        _viewModel.Parameters.ComfyUiCheckpoint.Should().BeEmpty("re-picking the default means no patch again");
    }

    [Fact]
    public async Task SavedCheckpoint_RestoredWhenStillOffered_ElseDefault()
    {
        SetupCheckpointService("baked.safetensors", "a.safetensors");
        _mockUiStateStore.Setup(s => s.LoadComfyUiCheckpoint(ComfyWorkflowName)).Returns("a.safetensors");

        SelectComfyWorkflow();
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);

        _viewModel.SelectedCheckpoint.Should().Be("a.safetensors");
        _viewModel.Parameters.ComfyUiCheckpoint.Should().Be("a.safetensors");
        // The restore is programmatic, not a user pick — it must not re-persist.
        _mockUiStateStore.Verify(s => s.PersistComfyUiCheckpoint(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        // A saved name the server no longer offers must never be invented into the selection.
        _mockUiStateStore.Setup(s => s.LoadComfyUiCheckpoint(ComfyWorkflowName)).Returns("vanished.safetensors");
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);

        _viewModel.SelectedCheckpoint.Should().Be("baked.safetensors");
        _viewModel.Parameters.ComfyUiCheckpoint.Should().BeEmpty();
    }

    [Fact]
    public async Task NonComfyModel_ClearsCheckpointState()
    {
        SetupCheckpointService("baked.safetensors", "a.safetensors");
        SelectComfyWorkflow();
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);
        _viewModel.SelectedCheckpoint = "a.safetensors";

        SelectIdeogramTurbo();
        await _viewModel.RefreshCheckpointOptionsAsync(ModelConstants.Ideogram.V4Turbo);

        _viewModel.SupportsCheckpoint.Should().BeFalse();
        _viewModel.CheckpointOptions.Should().BeEmpty();
        _viewModel.Parameters.ComfyUiCheckpoint.Should().BeEmpty(
            "a stale checkpoint must not linger in Clone() snapshots of other models");
    }

    [Fact]
    public async Task CheckpointNullSelectionPush_IsIgnored()
    {
        SetupCheckpointService("baked.safetensors", "a.safetensors");
        SelectComfyWorkflow();
        await _viewModel.RefreshCheckpointOptionsAsync(ComfyModelId);
        _viewModel.SelectedCheckpoint = "a.safetensors";

        // The WinUI ComboBox pushes SelectedItem=null on ItemsSource swaps.
        _viewModel.SelectedCheckpoint = null;

        _viewModel.Parameters.ComfyUiCheckpoint.Should().Be("a.safetensors",
            "a platform null push is a binding artifact, never a user pick");
        _mockUiStateStore.Verify(s => s.PersistComfyUiCheckpoint("a.safetensors", ComfyWorkflowName), Times.Once,
            "only the explicit pick persists");
    }

    // --- ComfyUI quality-preset picker (workflow's single CustomCombo node) -----------------
    // Mirrors the checkpoint picker, minus the async server fetch: the options come from the
    // workflow file itself, baked choice first (= no patch).

    private void SetupPresetService(ComfyUiQualityPresetSlot? slot) =>
        _mockCheckpointService
            .Setup(s => s.GetWorkflowQualityPresetSlotAsync(ComfyWorkflowName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(slot);

    private static ComfyUiQualityPresetSlot SamplePresetSlot() =>
        new("Default", ["Quality", "Default", "Turbo"]);

    [Fact]
    public async Task ComfyModel_WithSingleCombo_ShowsPresetPickerWithBakedChoiceFirst()
    {
        SetupPresetService(SamplePresetSlot());

        SelectComfyWorkflow();
        await _viewModel.RefreshQualityPresetOptionsAsync(ComfyModelId);

        _viewModel.SupportsQualityPreset.Should().BeTrue();
        // The baked choice leads and its duplicate among the options is removed.
        _viewModel.QualityPresetOptions.Should().Equal("Default", "Quality", "Turbo");
        _viewModel.SelectedQualityPreset.Should().Be("Default");
        _viewModel.Parameters.ComfyUiPreset.Should().BeEmpty("the baked selection means no patch");
        _viewModel.Parameters.ComfyUiPresetDisplay.Should().Be("Default",
            "the display field records the resolved preset even for the baked default");
    }

    [Fact]
    public async Task ComfyModel_WithoutSingleCombo_HidesPresetPicker()
    {
        // Covers no-combo, multi-combo, and link-driven choice — the probe returns null for
        // all of them.
        SetupPresetService(null);

        SelectComfyWorkflow();
        await _viewModel.RefreshQualityPresetOptionsAsync(ComfyModelId);

        _viewModel.SupportsQualityPreset.Should().BeFalse();
        _viewModel.QualityPresetOptions.Should().BeEmpty();
        _viewModel.Parameters.ComfyUiPreset.Should().BeEmpty();
    }

    [Fact]
    public async Task ExplicitPresetPick_WritesParametersAndPersists_BakedPickWritesEmpty()
    {
        SetupPresetService(SamplePresetSlot());
        SelectComfyWorkflow();
        await _viewModel.RefreshQualityPresetOptionsAsync(ComfyModelId);

        _viewModel.SelectedQualityPreset = "Turbo";

        _viewModel.Parameters.ComfyUiPreset.Should().Be("Turbo");
        _viewModel.Parameters.ComfyUiPresetDisplay.Should().Be("Turbo");
        _mockUiStateStore.Verify(s => s.PersistComfyUiPreset("Turbo", ComfyWorkflowName), Times.Once);

        _viewModel.SelectedQualityPreset = "Default";

        _viewModel.Parameters.ComfyUiPreset.Should().BeEmpty("re-picking the baked choice means no patch again");
        _viewModel.Parameters.ComfyUiPresetDisplay.Should().Be("Default",
            "but the display field still records it, so the metadata/card report the baked default");
    }

    [Fact]
    public async Task SavedPreset_RestoredWhenStillOffered_ElseBakedChoice()
    {
        SetupPresetService(SamplePresetSlot());
        _mockUiStateStore.Setup(s => s.LoadComfyUiPreset(ComfyWorkflowName)).Returns("Turbo");

        SelectComfyWorkflow();
        await _viewModel.RefreshQualityPresetOptionsAsync(ComfyModelId);

        _viewModel.SelectedQualityPreset.Should().Be("Turbo");
        _viewModel.Parameters.ComfyUiPreset.Should().Be("Turbo");
        // The restore is programmatic, not a user pick — it must not re-persist.
        _mockUiStateStore.Verify(s => s.PersistComfyUiPreset(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        // A saved label the workflow no longer offers must never be invented into the selection.
        _mockUiStateStore.Setup(s => s.LoadComfyUiPreset(ComfyWorkflowName)).Returns("Vanished");
        await _viewModel.RefreshQualityPresetOptionsAsync(ComfyModelId);

        _viewModel.SelectedQualityPreset.Should().Be("Default");
        _viewModel.Parameters.ComfyUiPreset.Should().BeEmpty();
    }

    [Fact]
    public async Task NonComfyModel_ClearsPresetState()
    {
        SetupPresetService(SamplePresetSlot());
        SelectComfyWorkflow();
        await _viewModel.RefreshQualityPresetOptionsAsync(ComfyModelId);
        _viewModel.SelectedQualityPreset = "Turbo";

        SelectIdeogramTurbo();
        await _viewModel.RefreshQualityPresetOptionsAsync(ModelConstants.Ideogram.V4Turbo);

        _viewModel.SupportsQualityPreset.Should().BeFalse();
        _viewModel.QualityPresetOptions.Should().BeEmpty();
        _viewModel.Parameters.ComfyUiPreset.Should().BeEmpty(
            "a stale preset must not linger in Clone() snapshots of other models");
        _viewModel.Parameters.ComfyUiPresetDisplay.Should().BeEmpty(
            "the display field is cleared too, so other models' cards don't show a stale preset");
    }

    [Fact]
    public async Task PresetNullSelectionPush_IsIgnored()
    {
        SetupPresetService(SamplePresetSlot());
        SelectComfyWorkflow();
        await _viewModel.RefreshQualityPresetOptionsAsync(ComfyModelId);
        _viewModel.SelectedQualityPreset = "Turbo";

        // The WinUI ComboBox pushes SelectedItem=null on ItemsSource swaps.
        _viewModel.SelectedQualityPreset = null;

        _viewModel.Parameters.ComfyUiPreset.Should().Be("Turbo",
            "a platform null push is a binding artifact, never a user pick");
        _mockUiStateStore.Verify(s => s.PersistComfyUiPreset("Turbo", ComfyWorkflowName), Times.Once,
            "only the explicit pick persists");
    }

    // --- CivitAI posting ---

    [Fact]
    public async Task Generate_WithPostToCivitaiChecked_PostsAfterSave()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _viewModel.Parameters.PostToCivitai = true;
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));
        var posted = new TaskCompletionSource();
        _mockCivitaiPostingService
            .Setup(s => s.PostImageAsync(FakeSavePath, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CivitaiPostResult(true, 1, "https://civitai.com/posts/1", "Posted to CivitAI (draft)."))
            .Callback(() => posted.TrySetResult());

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);
        // The posting hook is fire-and-forget by design; wait for the service call to land.
        await posted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        _viewModel.Jobs[0].Parameters.PostToCivitai.Should().BeTrue("the checkbox must survive the Clone() snapshot");
        _mockCivitaiPostingService.Verify(
            s => s.PostImageAsync(FakeSavePath, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Generate_WithPostToCivitaiUnchecked_NeverCallsThePostingService()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _mockCivitaiPostingService.Verify(
            s => s.PostImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PostJobToCivitai_Success_SetsStatusAndUrlOnTheJob()
    {
        var job = new GenerationJob(new ImageGenerationParameters { Prompt = "p", CivitaiIncludeMeta = true });
        _mockCivitaiPostingService
            .Setup(s => s.PostImageAsync(FakeSavePath, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CivitaiPostResult(true, 42, "https://civitai.com/posts/42", "Posted to CivitAI (draft)."));

        await _viewModel.PostJobToCivitaiAsync(job, FakeSavePath);

        job.CivitaiStatusMessage.Should().Be("Posted to CivitAI (draft).");
        job.CivitaiPostUrl.Should().Be("https://civitai.com/posts/42");
        // CivitaiIncludeMeta=true must translate into a non-null meta dictionary.
        _mockCivitaiPostingService.Verify(
            s => s.PostImageAsync(FakeSavePath, It.IsAny<string>(), It.Is<IReadOnlyDictionary<string, object>?>(m => m != null && (string)m["prompt"] == "p"), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostJobToCivitai_MetaUnchecked_PassesNullMeta()
    {
        var job = new GenerationJob(new ImageGenerationParameters { Prompt = "p", CivitaiIncludeMeta = false });
        _mockCivitaiPostingService
            .Setup(s => s.PostImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CivitaiPostResult(true, 1, "u", "ok"));

        await _viewModel.PostJobToCivitaiAsync(job, FakeSavePath);

        _mockCivitaiPostingService.Verify(
            s => s.PostImageAsync(FakeSavePath, It.IsAny<string>(), null, It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostJobToCivitai_Failure_LeavesJobStatusUntouched()
    {
        var job = new GenerationJob(new ImageGenerationParameters { Prompt = "p" })
        {
            StatusKind = StatusKind.Success,
            StatusMessage = "Saved.",
        };
        _mockCivitaiPostingService
            .Setup(s => s.PostImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CivitaiPostResult(false, null, null, "HTTP 500"));

        await _viewModel.PostJobToCivitaiAsync(job, FakeSavePath);

        job.CivitaiStatusMessage.Should().Be("CivitAI post failed: HTTP 500");
        job.CivitaiPostUrl.Should().BeNull();
        job.StatusKind.Should().Be(StatusKind.Success, "a failed CivitAI post must never demote a saved image");
        job.StatusMessage.Should().Be("Saved.");
    }

    [Fact]
    public async Task PostJobToCivitai_ServiceThrows_IsSwallowedIntoTheStatusLine()
    {
        var job = new GenerationJob(new ImageGenerationParameters { Prompt = "p" });
        _mockCivitaiPostingService
            .Setup(s => s.PostImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = () => _viewModel.PostJobToCivitaiAsync(job, FakeSavePath);

        await act.Should().NotThrowAsync("a fire-and-forget continuation must never take the app down");
        job.CivitaiStatusMessage.Should().Contain("boom");
    }

    [Fact]
    public async Task PostJobToCivitai_ModelRefSet_PassesParsedVersionId()
    {
        var job = new GenerationJob(new ImageGenerationParameters
        {
            Prompt = "p",
            CivitaiModelRef = "https://civitai.com/posts/create?modelId=2676710&modelVersionId=3005491&returnUrl=x",
        });
        _mockCivitaiPostingService
            .Setup(s => s.PostImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CivitaiPostResult(true, 1, "u", "Posted to CivitAI (draft)."));

        await _viewModel.PostJobToCivitaiAsync(job, FakeSavePath);

        _mockCivitaiPostingService.Verify(
            s => s.PostImageAsync(FakeSavePath, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), 3005491, It.IsAny<CancellationToken>()),
            Times.Once);
        job.CivitaiStatusMessage.Should().Be("Posted to CivitAI (draft).");
    }

    [Fact]
    public async Task PostJobToCivitai_UnrecognizableModelRef_PostsWithoutLinkAndSaysSo()
    {
        var job = new GenerationJob(new ImageGenerationParameters { Prompt = "p", CivitaiModelRef = "typo!!" });
        _mockCivitaiPostingService
            .Setup(s => s.PostImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CivitaiPostResult(true, 1, "u", "Posted to CivitAI (draft)."));

        await _viewModel.PostJobToCivitaiAsync(job, FakeSavePath);

        _mockCivitaiPostingService.Verify(
            s => s.PostImageAsync(FakeSavePath, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>?>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
        job.CivitaiStatusMessage.Should().Contain("Model reference not recognized");
    }

    [Fact]
    public void CivitaiModelRef_Changed_PersistsViaUiStateStore()
    {
        _viewModel.Parameters.CivitaiModelRef = "3005491";

        _mockUiStateStore.Verify(s => s.PersistCivitaiModelRef("3005491"), Times.Once);
    }

    [Fact]
    public void LoadSavedUiState_RestoresCivitaiModelRef()
    {
        _mockUiStateStore.Setup(s => s.LoadCivitaiModelRef()).Returns("3005491");

        _viewModel.LoadSavedUiState();

        _viewModel.Parameters.CivitaiModelRef.Should().Be("3005491");
    }

    [Fact]
    public async Task TestCivitaiConnection_WritesResultMessageToStatus()
    {
        _mockCivitaiPostingService
            .Setup(s => s.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CivitaiConnectionResult(true, "Connected as Silmas."));

        await ((IAsyncRelayCommand)_viewModel.TestCivitaiConnectionCommand).ExecuteAsync(null);

        _viewModel.CivitaiConnectionStatus.Should().Be("Connected as Silmas.");
    }

    // ---- Remix from an image ----------------------------------------------------------------

    private void StubRecipe(string path, params (string Key, string Value)[] lines)
    {
        var dict = lines.ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal);
        _mockGalleryService
            .Setup(s => s.ReadMetadataAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dict);
    }

    [Fact]
    public async Task RemixFromImage_FluxProRecipe_RestoresModelPromptSeedAndDisablesRandomize()
    {
        const string path = @"C:\fake\flux.png";
        StubRecipe(path,
            ("Prompt", "a remix prompt"),
            ("ModelName", ModelConstants.Flux.Pro11),
            ("Seed", "987654321"),
            ("AspectRatio", "3:2"),
            ("Format", "Jpg"),
            ("Quality", "80"),
            ("Upsampling", "True"));

        await _viewModel.RemixFromImageAsync(path);

        _viewModel.Parameters.Model.Should().Be(ModelConstants.Flux.Pro11);
        _viewModel.Parameters.Prompt.Should().Be("a remix prompt");
        _viewModel.Parameters.Seed.Should().Be(987654321);
        // Without this the seed would be re-rolled at generate time, defeating reproducibility.
        _viewModel.Parameters.RandomizeSeed.Should().BeFalse();
        _viewModel.Parameters.AspectRatio.Should().Be("3:2");
        _viewModel.Parameters.OutputFormat.Should().Be(ImageOutputFormat.Jpg);
        _viewModel.Parameters.OutputQuality.Should().Be(80);
        _viewModel.Parameters.PromptUpsampling.Should().BeTrue();
        _viewModel.StatusKind.Should().Be(StatusKind.Success);
    }

    [Fact]
    public async Task RemixFromImage_UnknownModel_LoadsPromptAndSeedOnly_WithWarning()
    {
        const string path = @"C:\fake\unknown.png";
        var originalModel = _viewModel.Parameters.Model;
        StubRecipe(path,
            ("Prompt", "orphan prompt"),
            ("ModelName", "vendor/deleted-model"),
            ("Seed", "42"));

        await _viewModel.RemixFromImageAsync(path);

        _viewModel.Parameters.Prompt.Should().Be("orphan prompt");
        _viewModel.Parameters.Seed.Should().Be(42);
        _viewModel.Parameters.RandomizeSeed.Should().BeFalse();
        _viewModel.Parameters.Model.Should().Be(originalModel);
        _viewModel.StatusKind.Should().Be(StatusKind.Warning);
    }

    [Fact]
    public async Task RemixFromImage_NoMetadata_SetsWarningAndChangesNothing()
    {
        const string path = @"C:\fake\nometa.png";
        var prompt = _viewModel.Parameters.Prompt;
        var model = _viewModel.Parameters.Model;
        _mockGalleryService
            .Setup(s => s.ReadMetadataAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string>?)null);

        await _viewModel.RemixFromImageAsync(path);

        _viewModel.Parameters.Prompt.Should().Be(prompt);
        _viewModel.Parameters.Model.Should().Be(model);
        _viewModel.StatusKind.Should().Be(StatusKind.Warning);
    }

    [Fact]
    public async Task RemixFromImage_IdeogramRecipe_AppliesResolutionAndJsonPromptWithoutPersisting()
    {
        const string path = @"C:\fake\ideogram.png";
        StubRecipe(path,
            ("Prompt", "{\"x\":1}"),
            ("ModelName", ModelConstants.Ideogram.V4Balanced),
            ("Resolution", "2048x2048"),
            ("JsonPrompt", "True"),
            ("CopyrightDetection", "True"));

        await _viewModel.RemixFromImageAsync(path);

        _viewModel.Parameters.Model.Should().Be(ModelConstants.Ideogram.V4Balanced);
        _viewModel.Parameters.Resolution.Should().Be("2048x2048");
        _viewModel.Parameters.UseJsonPrompt.Should().BeTrue();
        _viewModel.Parameters.EnableCopyrightDetection.Should().BeTrue();
        // A programmatic restore must never overwrite the user's persisted preferences.
        _mockUiStateStore.Verify(s => s.PersistResolution(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockUiStateStore.Verify(s => s.PersistUseJsonPrompt(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task RemixFromImage_AspectRatioNotOfferedByModel_IsIgnored()
    {
        // gpt-image-1.5 only offers 1:1 / 3:2 / 2:3 — a saved "21:9" must not slip through.
        const string path = @"C:\fake\gpt.png";
        StubRecipe(path,
            ("Prompt", "p"),
            ("ModelName", ModelConstants.OpenAI.GptImage15OnReplicate),
            ("AspectRatio", "21:9"));

        await _viewModel.RemixFromImageAsync(path);

        _viewModel.Capabilities.AspectRatios.Should().Contain(_viewModel.Parameters.AspectRatio);
        _viewModel.Parameters.AspectRatio.Should().NotBe("21:9");
    }

    [Fact]
    public async Task RemixFromImage_ThenGenerate_PreservesTheRemixedSeed()
    {
        const string path = @"C:\fake\seed.png";
        StubRecipe(path,
            ("Prompt", "seed test"),
            ("ModelName", ModelConstants.Flux.Pro11),
            ("Seed", "123456"));

        await _viewModel.RemixFromImageAsync(path);
        _viewModel.Parameters.ApiToken = "valid-token";

        ImageGenerationParameters? captured = null;
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .Callback<ImageGenerationParameters, CancellationToken, IProgress<JobProgress>?>((p, _, _) => captured = p)
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, "ok"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.Seed.Should().Be(123456);
    }

    // ---- Clear finished jobs ----------------------------------------------------------------

    private static GenerationJob Job(bool isRunning, StatusKind kind, string? resultPath = null) =>
        new(new ImageGenerationParameters()) { IsRunning = isRunning, StatusKind = kind, ResultPath = resultPath };

    [Fact]
    public void ClearFinishedJobs_RemovesTerminalJobs_KeepsRunningAndQueued()
    {
        var running = Job(isRunning: true, StatusKind.Info);
        var queued = Job(isRunning: false, StatusKind.Info);   // batch "Queued (i/n)" shape
        var saved = Job(isRunning: false, StatusKind.Success, resultPath: "x.png");
        var failed = Job(isRunning: false, StatusKind.Error);
        var canceled = Job(isRunning: false, StatusKind.Canceled);
        foreach (var j in new[] { running, queued, saved, failed, canceled }) _viewModel.Jobs.Add(j);

        _viewModel.ClearFinishedJobsCommand.Execute(null);

        _viewModel.Jobs.Should().HaveCount(2);
        _viewModel.Jobs.Should().Contain(running).And.Contain(queued);
        _viewModel.Jobs.Should().NotContain(saved).And.NotContain(failed).And.NotContain(canceled);
    }

    [Fact]
    public async Task ClearFinishedJobs_ResetsLatestCompletedJobAndEmptiesQueue()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "p";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<JobProgress>?>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, "ok"));
        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);
        _viewModel.LatestCompletedJob.Should().NotBeNull();

        _viewModel.ClearFinishedJobsCommand.Execute(null);

        _viewModel.Jobs.Should().BeEmpty();
        _viewModel.LatestCompletedJob.Should().BeNull("clearing finished jobs removes the featured card");
        _viewModel.HasJobs.Should().BeFalse();
    }

    [Fact]
    public void HasFinishedJobs_GatesTheClearCommand()
    {
        _viewModel.ClearFinishedJobsCommand.CanExecute(null).Should().BeFalse("empty queue");

        _viewModel.Jobs.Add(Job(isRunning: true, StatusKind.Info));
        _viewModel.Jobs.Add(Job(isRunning: false, StatusKind.Info));   // queued
        _viewModel.HasFinishedJobs.Should().BeFalse();
        _viewModel.ClearFinishedJobsCommand.CanExecute(null).Should().BeFalse();

        _viewModel.Jobs.Add(Job(isRunning: false, StatusKind.Success, resultPath: "x.png"));
        _viewModel.HasFinishedJobs.Should().BeTrue();
        _viewModel.ClearFinishedJobsCommand.CanExecute(null).Should().BeTrue();
    }
}
