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
        _viewModel.GptComplexityOptions.Should().BeEquivalentTo("low", "medium", "high");
        // Empty list, hidden row — not a labelled Picker with nothing in it.
        _viewModel.GptInputFidelityOptions.Should().BeEmpty();
        _viewModel.GptQualityOptions.Should().Contain("xhigh").And.Contain("max");
        _viewModel.AspectRatioOptions.Should().HaveCount(18).And.Contain("1024x1024").And.Contain("auto");
    }

    [Fact]
    public void Capabilities_GptImage15_StillHasInputFidelityAndNoComplexity()
    {
        SelectModel(ModelConstants.OpenAI.GptImage15OnReplicate);

        _viewModel.SupportsGptInputFidelity.Should().BeTrue();
        _viewModel.SupportsGptComplexity.Should().BeFalse();
        _viewModel.GptComplexityOptions.Should().BeEmpty();
        _viewModel.AspectRatioOptions.Should().BeEquivalentTo("1:1", "3:2", "2:3");
    }

    [Fact]
    public void SwitchingFromSunburst_SnapsQualityAndAspectRatioTheNewModelRejects()
    {
        SelectModel(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);
        _viewModel.Parameters.GptQuality = "xhigh";
        _viewModel.Parameters.AspectRatio = "1024x1024";

        SelectModel(ModelConstants.OpenAI.GptImage15OnReplicate);

        // Both values exist only on 2.6. Carrying them into gpt-image-1.5 is a 422 on a field the
        // picker no longer even shows.
        _viewModel.Parameters.GptQuality.Should().Be("auto");
        _viewModel.Parameters.AspectRatio.Should().Be("1:1");
    }

    [Fact]
    public void SwitchingToSunburst_KeepsEveryKnobTheModelAlsoOffers()
    {
        SelectModel(ModelConstants.OpenAI.GptImage15OnReplicate);
        _viewModel.Parameters.GptQuality = "high";
        _viewModel.Parameters.GptBackground = "transparent";
        _viewModel.Parameters.GptModeration = "low";
        _viewModel.Parameters.AspectRatio = "3:2";

        SelectModel(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);

        // Snapping is a capability fallback, not a reset: a selection the new model accepts and the
        // user just made has to survive the switch.
        _viewModel.Parameters.GptQuality.Should().Be("high");
        _viewModel.Parameters.GptBackground.Should().Be("transparent");
        _viewModel.Parameters.GptModeration.Should().Be("low");
        _viewModel.Parameters.AspectRatio.Should().Be("3:2");
        // …and the knob that only exists here lands on the model's own default.
        _viewModel.Parameters.GptComplexity.Should().Be("high");
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
