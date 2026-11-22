using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.ViewModels;

/// <summary>
/// The hosted gpt-image knobs as the user sees them: which pickers a model shows, and what happens
/// to a knob when the next model does not offer its value.
///
/// Both halves matter. A picker for a field the model never declared is a control that can only
/// ever be wrong, and a knob that keeps a value the new model rejects is an HTTP 422 on a field the
/// user cannot see — which is how openai/gpt-image-2.6-sunburst first failed.
/// Part of <see cref="GeneratorViewModelTests"/> to reuse its mocks.
/// </summary>
public partial class GeneratorViewModelTests
{
    private void SelectModel(string modelId) =>
        _viewModel.ProviderFilter.SelectedModel =
            _viewModel.ProviderFilter.AllModels.First(m => m.Value == modelId);

    [Fact]
    public void Capabilities_GptImage26Sunburst_ExposesComplexityAndDropsInputFidelity()
    {
        SelectModel(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);

        _viewModel.SupportsGptQuality.Should().BeTrue();
        _viewModel.SupportsGptComplexity.Should().BeTrue();
        _viewModel.SupportsGptInputFidelity.Should().BeFalse();
        _viewModel.GptComplexityChoice.Options.Should().BeEquivalentTo("low", "medium", "high");
        // Empty list, hidden row — not a labelled Picker with nothing in it.
        _viewModel.GptInputFidelityChoice.Options.Should().BeEmpty();
        _viewModel.GptQualityChoice.Options.Should().Contain("xhigh").And.Contain("max");
        _viewModel.AspectRatioChoice.Options.Should().HaveCount(18).And.Contain("1024x1024").And.Contain("auto");
    }

    [Fact]
    public void Capabilities_GptImage15_StillHasInputFidelityAndNoComplexity()
    {
        SelectModel(ModelConstants.OpenAI.GptImage15OnReplicate);

        _viewModel.SupportsGptInputFidelity.Should().BeTrue();
        _viewModel.SupportsGptComplexity.Should().BeFalse();
        _viewModel.GptComplexityChoice.Options.Should().BeEmpty();
        _viewModel.AspectRatioChoice.Options.Should().BeEquivalentTo("1:1", "3:2", "2:3");
    }

    [Fact]
    public void SwitchingFromSunburst_SnapsQualityAndAspectRatioTheNewModelRejects()
    {
        SelectModel(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);
        _viewModel.Parameters.GptQuality = "xhigh";
        _viewModel.Parameters.AspectRatio = "1024x1024";

        SelectModel(ModelConstants.OpenAI.GptImage15OnReplicate);

        // Both values exist only on 2.6. Carrying them into gpt-image-1.5 is a 422 on a field the
        // picker no longer even shows. The value it lands on is this app's own starting value, not
        // gpt-image-1.5's first enum value — that one is "auto", and "auto" is the priced end.
        _viewModel.Parameters.GptQuality.Should().Be(ImageGenerationParameters.DefaultGptQuality);
        _viewModel.Parameters.AspectRatio.Should().Be("1:1");
    }

    [Fact]
    public void SwitchingAwayAndBack_ReturnsTheGptKnobsToTheAppsStartingValues()
    {
        // SelectedItem is bound two-way to these knobs (MainPage), and a Picker reacts to its
        // ItemsSource being replaced by writing null into the bound value — the same hazard that
        // made FallbackReplicateDescriptor keep a broad aspect-ratio list. So visiting any model
        // with no gpt knobs clears them; the snap on the way back then had to choose something, and
        // choosing the schema's first enum value chose "auto" for quality and moderation both.
        // The nulls are written by hand because no Picker runs in a unit test.
        SelectModel(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);
        _viewModel.Parameters.GptQuality = "high";

        SelectModel(ModelConstants.Flux.Klein4b);
        _viewModel.Parameters.GptQuality = null!;
        _viewModel.Parameters.GptModeration = null!;
        _viewModel.Parameters.GptComplexity = null!;

        SelectModel(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);

        _viewModel.Parameters.GptQuality.Should().Be(ImageGenerationParameters.DefaultGptQuality);
        _viewModel.Parameters.GptModeration.Should().Be(ImageGenerationParameters.DefaultGptModeration);
        _viewModel.Parameters.GptComplexity.Should().Be(ImageGenerationParameters.DefaultGptComplexity);
    }

    [Fact]
    public void SwitchingToSunburst_KeepsEveryKnobTheModelAlsoOffers()
    {
        SelectModel(ModelConstants.OpenAI.GptImage15OnReplicate);
        _viewModel.GptQualityChoice.Selected = "high";
        _viewModel.GptBackgroundChoice.Selected = "transparent";
        _viewModel.GptModerationChoice.Selected = "low";
        _viewModel.AspectRatioChoice.Selected = "3:2";

        SelectModel(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);

        // Snapping is a capability fallback, not a reset: a selection the new model accepts and the
        // user just made has to survive the switch.
        _viewModel.Parameters.GptQuality.Should().Be("high");
        _viewModel.Parameters.GptBackground.Should().Be("transparent");
        _viewModel.Parameters.GptModeration.Should().Be("low");
        _viewModel.Parameters.AspectRatio.Should().Be("3:2");
        // …and the knob that exists only here stays on the value the app starts it at — deliberately
        // not that model's server default, which is "high": slower, and free to change parts of the
        // image nobody mentioned.
        _viewModel.Parameters.GptComplexity.Should().Be("low");
    }

    [Fact]
    public void RefreshCapabilities_SnapsEveryGptKnobANewModelDoesNotOffer()
    {
        // No two shipped models disagree about background / moderation / input fidelity yet, so
        // those three clamps are invisible in production today — which is exactly why they need a
        // stub model: the 422 they prevent is one schema refresh away, and quality already proved
        // the pattern (Pollinations has no "auto", 2.6 adds "xhigh"/"max").
        var wide = new StubKnobDescriptor("wide", ["auto", "transparent"], ["auto", "low"], ["low", "high"]);
        var narrow = new StubKnobDescriptor("narrow", ["transparent"], ["low"], ["high"]);
        var vm = CreateViewModel(new ModelDescriptorRegistry(
            [wide, narrow],
            [wide, narrow],
            [],
            [wide, narrow]));

        vm.ProviderFilter.SelectedModel = vm.ProviderFilter.AllModels.First(m => m.Value == "wide");
        vm.Parameters.GptBackground = "auto";
        vm.Parameters.GptModeration = "auto";
        vm.Parameters.GptInputFidelity = "low";

        vm.ProviderFilter.SelectedModel = vm.ProviderFilter.AllModels.First(m => m.Value == "narrow");

        vm.Parameters.GptBackground.Should().Be("transparent");
        vm.Parameters.GptModeration.Should().Be("low");
        vm.Parameters.GptInputFidelity.Should().Be("high");
    }

    /// <summary>A model that declares a deliberately narrower knob vocabulary than any real one.</summary>
    private sealed class StubKnobDescriptor(
        string modelId,
        string[] background,
        string[] moderation,
        string[] fidelity) : IPayloadBuilder, ICapabilityProvider, ICatalogSeedEntry
    {
        public string ModelId => modelId;

        public ModelCapabilities Capabilities => new(
            SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
            AspectRatio: true, CustomDimensions: false, Seed: false, ImagePrompt: false,
            AspectRatioLabel: "Aspect ratio", AspectRatios: ["1:1"],
            GptQualityOptions: ["auto"],
            GptBackgroundOptions: background,
            GptModerationOptions: moderation,
            GptInputFidelityOptions: fidelity);

        public ModelOption Seed => new(modelId, modelId, ProviderConstants.Replicate);

        public object Build(ImageGenerationParameters parameters) => new Dictionary<string, object?>();
    }
}
