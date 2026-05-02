using FluentAssertions;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Moq;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.ViewModels;

public class GeneratorViewModelTests
{
    private const string FakeSavePath = @"C:\fake\generated.png";

    private readonly GeneratorViewModel _viewModel;
    private readonly Mock<IJobRunner> _mockJobRunner;
    private readonly Mock<IApiTokenStore> _mockTokenStore;
    private readonly Mock<IModelCatalogCoordinator> _mockCatalogCoordinator;

    public GeneratorViewModelTests()
    {
        _mockJobRunner = new Mock<IJobRunner>();
        _mockTokenStore = new Mock<IApiTokenStore>();
        _mockCatalogCoordinator = new Mock<IModelCatalogCoordinator>();

        _viewModel = new GeneratorViewModel(
            _mockJobRunner.Object,
            _mockTokenStore.Object,
            _mockCatalogCoordinator.Object,
            ModelDescriptorRegistry.Default());
    }

    [Fact]
    public void AllModels_ShouldContainExpectedSeed()
    {
        var values = _viewModel.AllModels.Select(m => m.Value).ToList();
        values.Should().Contain(ModelConstants.OpenAI.GptImage15OnReplicate);
        values.Should().Contain(ModelConstants.Flux.Pro11);
        values.Should().Contain(ModelConstants.Flux.Pro11Ultra);
        values.Should().Contain(ModelConstants.Flux.Klein4b);
        values.Should().Contain(ModelConstants.Google.NanoBanana2);
    }

    [Fact]
    public void Providers_ShouldIncludeAllAndDistinctProviders()
    {
        _viewModel.Providers.Should().Contain("All providers");
        _viewModel.Providers.Should().Contain("OpenAI (via Replicate)");
        _viewModel.Providers.Should().Contain("Black Forest Labs");
        _viewModel.Providers.Should().Contain("Google");
    }

    [Fact]
    public void SelectedProvider_WhenSet_FiltersModels()
    {
        _viewModel.SelectedProvider = "Google";

        _viewModel.FilteredModels.Should().OnlyContain(m => m.Provider == "Google");
        _viewModel.FilteredModels.Should().HaveCount(1);
    }

    [Fact]
    public void SelectedModel_WhenChanged_UpdatesParametersModel()
    {
        var target = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);

        _viewModel.SelectedModel = target;

        _viewModel.Parameters.Model.Should().Be(ModelConstants.Flux.Klein4b);
    }

    [Fact]
    public async Task GenerateImage_WithValidParameters_ShouldGenerateImage()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";

        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Saved, FakeSavePath, $"Saved to {FakeSavePath}"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.Jobs.Should().HaveCount(1);
        _viewModel.Jobs[0].StatusMessage.Should().StartWith("Saved to ");
        _viewModel.Jobs[0].StatusKind.Should().Be(StatusKind.Success);
        _viewModel.Jobs[0].ResultPath.Should().NotBeNull();
        _viewModel.Jobs[0].IsRunning.Should().BeFalse();
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
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);
        _viewModel.AspectRatioOptions.Should().Contain("match_input_image");

        _viewModel.SelectedImages.Add(FakeImage("a"));

        _viewModel.Parameters.AspectRatio.Should().Be("match_input_image");
    }

    [Fact]
    public void RemoveLastImage_OnFlux2Model_FallsBackToFirstAspectRatio()
    {
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);
        _viewModel.SelectedImages.Add(FakeImage("a"));
        _viewModel.Parameters.AspectRatio.Should().Be("match_input_image");

        _viewModel.SelectedImages.Clear();

        _viewModel.Parameters.AspectRatio.Should().NotBe("match_input_image");
    }

    private static GeneratorViewModel.InputImageItem FakeImage(string base64 = "abc", string name = "test.png") =>
        new(base64, null, name);

    [Fact]
    public void SelectedModel_Changed_AdjustsAspectRatioOptionsToSupportedList()
    {
        // Switching from Flux 1.1 Pro (has "custom") to Klein 4B (no "custom") must narrow the list.
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);

        _viewModel.AspectRatioOptions.Should().NotContain("custom");
        _viewModel.AspectRatioOptions.Should().Contain("1:1");
    }

    [Fact]
    public async Task GenerateImage_WhenJobRunnerThrowsException_ShouldHandleError()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>()))
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
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == modelValue);

        _viewModel.SupportsSafetyTolerance.Should().Be(safety);
        _viewModel.SupportsPromptUpsampling.Should().Be(upsampling);
        _viewModel.SupportsOutputQuality.Should().Be(outputQuality);
        _viewModel.SupportsAspectRatio.Should().Be(aspectRatio);
        _viewModel.SupportsSeed.Should().Be(seed);
        _viewModel.SupportsImagePrompt.Should().Be(imagePrompt);
    }

    [Fact]
    public void Capabilities_NanoBanana2_ExposesResolution()
    {
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Google.NanoBanana2);

        _viewModel.SupportsResolution.Should().BeTrue();
        _viewModel.ResolutionOptions.Should().BeEquivalentTo("1K", "2K", "4K");
    }

    [Fact]
    public void Capabilities_GptImage15_ExposesAllFourGptOptionLists()
    {
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.OpenAI.GptImage15OnReplicate);

        _viewModel.SupportsGptQuality.Should().BeTrue();
        _viewModel.SupportsGptBackground.Should().BeTrue();
        _viewModel.SupportsGptModeration.Should().BeTrue();
        _viewModel.SupportsGptInputFidelity.Should().BeTrue();
        _viewModel.GptQualityOptions.Should().BeEquivalentTo("auto", "low", "medium", "high");
        _viewModel.GptBackgroundOptions.Should().BeEquivalentTo("auto", "transparent", "opaque");
        _viewModel.GptModerationOptions.Should().BeEquivalentTo("auto", "low");
        _viewModel.GptInputFidelityOptions.Should().BeEquivalentTo("low", "high");
    }

    [Fact]
    public void Capabilities_NonGptModel_HidesGptOptionLists()
    {
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);

        _viewModel.SupportsGptQuality.Should().BeFalse();
        _viewModel.SupportsGptBackground.Should().BeFalse();
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
            new("flux-2", "black-forest-labs/flux-2", "Black Forest Labs"),
            new("gpt-image-1.5", "openai/gpt-image-1.5", "OpenAI (via Replicate)")
        };
        _mockCatalogCoordinator
            .Setup(x => x.RefreshAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged);

        await ((IAsyncRelayCommand)_viewModel.RefreshModelsCommand).ExecuteAsync(null);

        _viewModel.AllModels.Select(m => m.Value)
            .Should().Contain("black-forest-labs/flux-2")
            .And.Contain("openai/gpt-image-1.5");
        _viewModel.StatusKind.Should().Be(StatusKind.Success);
    }

    [Fact]
    public async Task LoadCachedCatalog_WithMergedResult_IngestsModelsAndProviders()
    {
        var merged = new List<ModelOption>
        {
            new("flux-2-pro", "black-forest-labs/flux-2-pro", "Black Forest Labs"),
            new("gpt-image-1.5", "openai/gpt-image-1.5", "OpenAI (via Replicate)")
        };
        _mockCatalogCoordinator
            .Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged);

        await _viewModel.LoadCachedCatalogAsync();

        _viewModel.AllModels.Select(m => m.Value)
            .Should().Contain("black-forest-labs/flux-2-pro")
            .And.Contain("openai/gpt-image-1.5");
        _viewModel.Providers.Should().Contain("Black Forest Labs")
            .And.Contain("OpenAI (via Replicate)");
    }

    [Fact]
    public async Task LoadCachedCatalog_WhenCoordinatorReturnsNull_KeepsHardcodedSeed()
    {
        var seed = _viewModel.AllModels.ToList();
        _mockCatalogCoordinator
            .Setup(x => x.LoadCachedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModelOption>?)null);

        await _viewModel.LoadCachedCatalogAsync();

        _viewModel.AllModels.Should().BeEquivalentTo(seed);
    }

    [Fact]
    public async Task LoadCachedCatalog_RunningTwice_IgnoresSecondInvocation()
    {
        var merged = new List<ModelOption>
        {
            new("flux-2", "black-forest-labs/flux-2", "Black Forest Labs")
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
            new("flux-2-pro", "black-forest-labs/flux-2-pro", "Black Forest Labs"),
            new("gpt-image-1.5", "openai/gpt-image-1.5", "OpenAI (via Replicate)"),
            new("nano-banana-2", "google/nano-banana-2", "Google")
        };
        _mockCatalogCoordinator
            .Setup(x => x.RefreshAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged);

        await ((IAsyncRelayCommand)_viewModel.RefreshModelsCommand).ExecuteAsync(null);

        // Regression: FilteredModels must reflect freshly-applied AllModels, not be stuck on the
        // pre-refresh list.
        _viewModel.FilteredModels.Select(m => m.Value)
            .Should().Contain("black-forest-labs/flux-2-pro")
            .And.Contain("openai/gpt-image-1.5")
            .And.Contain("google/nano-banana-2");
        _viewModel.Providers.Should().Contain("Black Forest Labs")
            .And.Contain("OpenAI (via Replicate)")
            .And.Contain("Google");
    }

    [Fact]
    public async Task RefreshModels_EmptyToken_SetsErrorStatusWithoutFetching()
    {
        _viewModel.Parameters.ApiToken = "";

        await ((IAsyncRelayCommand)_viewModel.RefreshModelsCommand).ExecuteAsync(null);

        _viewModel.StatusKind.Should().Be(StatusKind.Error);
        _mockCatalogCoordinator.Verify(
            x => x.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshModels_NullResult_KeepsExistingCatalogAndReportsError()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        var originalModels = _viewModel.AllModels.ToList();
        _mockCatalogCoordinator
            .Setup(x => x.RefreshAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ModelOption>?)null);

        await ((IAsyncRelayCommand)_viewModel.RefreshModelsCommand).ExecuteAsync(null);

        _viewModel.AllModels.Should().BeEquivalentTo(originalModels);
        _viewModel.StatusKind.Should().Be(StatusKind.Error);
    }

    [Fact]
    public async Task GenerateImage_WhenJobRunnerReturnsCanceledMessage_ShouldSetCanceledKind()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockJobRunner
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobOutcome(JobOutcomeKind.Failed, null, "Image generation was canceled."));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.Jobs.Should().HaveCount(1);
        _viewModel.Jobs[0].StatusKind.Should().Be(StatusKind.Canceled);
        _viewModel.Jobs[0].ResultPath.Should().BeNull();
    }

    [Fact]
    public void ApiToken_Change_ForwardsToTokenStorePersist()
    {
        _viewModel.Parameters.ApiToken = "fresh-token";

        _mockTokenStore.Verify(x => x.Persist("fresh-token"), Times.Once);
    }

    [Fact]
    public void ForgetToken_ClearsParametersAndCallsTokenStoreForget()
    {
        _viewModel.Parameters.ApiToken = "some-token";

        _viewModel.ForgetTokenCommand.Execute(null);

        _viewModel.Parameters.ApiToken.Should().BeEmpty();
        _mockTokenStore.Verify(x => x.Forget(), Times.Once);
    }

    [Fact]
    public async Task LoadSavedTokenAsync_PopulatesParametersFromStore()
    {
        _mockTokenStore.Setup(x => x.LoadAsync()).ReturnsAsync("saved-token");

        await _viewModel.LoadSavedTokenAsync();

        _viewModel.Parameters.ApiToken.Should().Be("saved-token");
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
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == model);

        _viewModel.ImagePromptCardTitle.Should().Be(expected);
    }

    [Fact]
    public void SupportsImagePromptStrength_RequiresBothUltraModelAndAtLeastOneImage()
    {
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11Ultra);
        _viewModel.SupportsImagePromptStrength.Should().BeFalse("no image yet");

        _viewModel.SelectedImages.Add(FakeImage("a"));
        _viewModel.SupportsImagePromptStrength.Should().BeTrue();

        _viewModel.SelectedImages.Clear();
        _viewModel.SupportsImagePromptStrength.Should().BeFalse("image removed");
    }

    [Fact]
    public void SupportsImagePromptStrength_NonUltraModel_StaysFalseEvenWithImages()
    {
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Klein4b);
        _viewModel.SelectedImages.Add(FakeImage("a"));

        _viewModel.SupportsImagePromptStrength.Should().BeFalse();
    }

    // --- Multi-image commands ---

    [Fact]
    public async Task AddImageCommand_AtCap_SetsErrorStatus_AndDoesNotOpenPicker()
    {
        // Flux 1.1 Pro has MaxImageInputs = 1.
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);
        _viewModel.SelectedImages.Add(FakeImage("a"));

        _viewModel.CanAddImage.Should().BeFalse();
        await ((IAsyncRelayCommand)_viewModel.AddImageCommand).ExecuteAsync(null);

        _viewModel.StatusKind.Should().Be(StatusKind.Error);
        _viewModel.StatusMessage.Should().Contain("Maximum");
        _viewModel.SelectedImages.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveImageCommand_RemovesOnlyTargetedItem()
    {
        var a = FakeImage("a", "a.png");
        var b = FakeImage("b", "b.png");
        var c = FakeImage("c", "c.png");
        _viewModel.SelectedImages.Add(a);
        _viewModel.SelectedImages.Add(b);
        _viewModel.SelectedImages.Add(c);

        _viewModel.RemoveImageCommand.Execute(b);

        _viewModel.SelectedImages.Should().ContainInOrder(a, c);
        _viewModel.SelectedImages.Should().NotContain(b);
    }

    [Fact]
    public void ClearImagesCommand_EmptiesCollection()
    {
        _viewModel.SelectedImages.Add(FakeImage("a"));
        _viewModel.SelectedImages.Add(FakeImage("b"));

        _viewModel.ClearImagesCommand.Execute(null);

        _viewModel.SelectedImages.Should().BeEmpty();
    }

    [Fact]
    public void SwitchModelWithNarrowerCap_TruncatesSelectedImagesToNewCap()
    {
        // Start on nano-banana-2 (cap 14), load 5 images, switch to Flux 1.1 Pro (cap 1).
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Google.NanoBanana2);
        for (var i = 0; i < 5; i++) _viewModel.SelectedImages.Add(FakeImage($"img{i}"));
        _viewModel.SelectedImages.Should().HaveCount(5);

        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Pro11);

        _viewModel.SelectedImages.Should().HaveCount(1);
        _viewModel.CanAddImage.Should().BeFalse();
    }

    [Fact]
    public void CanAddImage_FlipsAtExactlyTheCap()
    {
        // gpt-image-1.5: cap is 10.
        _viewModel.SelectedModel = _viewModel.AllModels.First(m => m.Value == ModelConstants.OpenAI.GptImage15OnReplicate);

        for (var i = 0; i < 9; i++)
        {
            _viewModel.SelectedImages.Add(FakeImage($"img{i}"));
            _viewModel.CanAddImage.Should().BeTrue($"only {i + 1} of 10 added");
        }

        _viewModel.SelectedImages.Add(FakeImage("img10"));
        _viewModel.CanAddImage.Should().BeFalse("reached cap");
    }

    [Fact]
    public void ImagePrompts_MirrorsSelectedImagesOnChange()
    {
        _viewModel.SelectedImages.Add(FakeImage("a", "a.png"));
        _viewModel.SelectedImages.Add(FakeImage("b", "b.png"));

        _viewModel.Parameters.ImagePrompts.Should().ContainInOrder("a", "b");

        _viewModel.SelectedImages.RemoveAt(0);
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
            .Setup(x => x.RunAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>()))
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
}
