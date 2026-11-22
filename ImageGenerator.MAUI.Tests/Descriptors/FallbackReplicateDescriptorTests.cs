using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;

namespace ImageGenerator.MAUI.Tests.Descriptors;

/// <summary>
/// What this build can claim about a Replicate model it has no descriptor for. Such a model is
/// never sent a request — see ReplicateImageGenerationServiceTests for the refusal itself, which
/// is the behaviour these capabilities exist to explain in the UI.
///
/// Both tests here are about the pickers, not about the model: a hidden knob is a claim ("this model
/// has no such field") that must not be mistaken for a guess, and one of them is keeping a two-way
/// bound Picker from rewriting saved state.
/// </summary>
public class FallbackReplicateDescriptorTests
{
    private readonly FallbackReplicateDescriptor _descriptor = new();

    [Fact]
    public void Capabilities_OfferNoKnobForAFieldWeCannotSend()
    {
        var caps = _descriptor.Capabilities;

        caps.Seed.Should().BeFalse();
        caps.AspectRatio.Should().BeFalse();
        caps.OutputQuality.Should().BeFalse();
        caps.SafetyTolerance.Should().BeFalse();
        caps.PromptUpsampling.Should().BeFalse();
        caps.CustomDimensions.Should().BeFalse();
        caps.IdeogramOptions.Should().BeFalse();
        caps.JsonPromptEditor.Should().BeFalse();
        caps.GptQualityOptions.Should().BeNull();
        caps.GptComplexityOptions.Should().BeNull();
        // A reference image is the knob worth naming, because accepting one promises an edit: the
        // Input Image card and the gallery's "use as input image" both read ImagePrompt, so offering
        // it here would have the user attach a picture to a model this build refuses to call.
        caps.ImagePrompt.Should().BeFalse();
        caps.MaxImageInputs.Should().Be(0);
        // Nothing is ever generated for such a model, so there is nothing to save in a chosen format.
        caps.OutputFormatSelectable.Should().BeFalse();
    }

    [Fact]
    public void Capabilities_OfferNoAspectRatios()
    {
        // Nothing is sent to this model, so it has no ratios to offer. What that does to the
        // generator's ratio is pinned in GeneratorViewModelTests.UnknownModel.
        var caps = _descriptor.Capabilities;

        caps.AspectRatio.Should().BeFalse();
        caps.AspectRatios.Should().BeEmpty();
    }
}
