using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Tests.Descriptors;

/// <summary>
/// The fallback payload is what gets sent to a model this build has never seen, so its contract is
/// "the smallest request every Replicate image model accepts". These tests pin the key set in both
/// directions: the two removed-by-mistake directions (a field a model rejects, a field a model
/// silently drops) and the one thing that must always survive.
/// </summary>
public class FallbackReplicateDescriptorTests
{
    private readonly FallbackReplicateDescriptor _descriptor = new();

    private static Dictionary<string, object?> Build(params string[] imagePrompts)
    {
        var parameters = new ImageGenerationParameters { Model = "someone/new-model", Prompt = "a red cube" };
        foreach (var image in imagePrompts) parameters.ImagePrompts.Add(image);
        return (Dictionary<string, object?>)new FallbackReplicateDescriptor().Build(parameters);
    }

    [Fact]
    public void Build_WithoutAttachments_SendsThePromptAndNothingElse()
    {
        Build().Should().Equal(new Dictionary<string, object?> { ["prompt"] = "a red cube" });
    }

    [Fact]
    public void Build_WithAttachment_SendsItUnderBothNamesWithoutAskingWhich()
    {
        // PNG magic bytes, so the encoder sniffs image/png rather than guessing.
        const string base64Png = "iVBORw0KGgo=";

        var input = Build(base64Png);

        input.Should().ContainKeys("images", "input_images");
        // The OpenAI family declares `input_images`, most others `images`; cog drops whichever one
        // the model doesn't declare, so both go and the model picks its own.
        input["images"].Should().BeSameAs(input["input_images"]);
        ((string[])input["images"]!)[0].Should().Be("data:image/png;base64,iVBORw0KGgo=");
    }

    [Fact]
    public void Build_WithSeveralAttachments_StillSendsOnlyOne()
    {
        var input = Build("iVBORw0KGgo=", "iVBORw0KGgo=", "iVBORw0KGgo=");

        ((string[])input["images"]!).Should().HaveCount(1,
            "the fallback advertises MaxImageInputs 1; sending more would send what the UI never offered");
    }

    [Theory]
    // Every one of these used to be sent, and every one was wrong: `aspect_ratio` carried an
    // invented vocabulary (422), `output_format` sent "jpg" where models declare "jpeg" (422),
    // `seed` and `output_quality` name fields these models mostly do not declare (silently dropped,
    // so the knob looked live and did nothing).
    [InlineData("seed")]
    [InlineData("aspect_ratio")]
    [InlineData("output_format")]
    [InlineData("output_quality")]
    [InlineData("safety_tolerance")]
    [InlineData("prompt_upsampling")]
    [InlineData("number_of_images")]
    public void Build_SendsNoFieldTheFallbackCannotKnowExists(string key) =>
        Build().Should().NotContainKey(key);

    [Fact]
    public void Capabilities_OfferOnlyWhatBuildSends()
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

        caps.ImagePrompt.Should().BeTrue();
        caps.MaxImageInputs.Should().Be(1);
        // Output format is honoured by the app's own transcode on save, so it stays honest to offer.
        caps.OutputFormatSelectable.Should().BeTrue();
    }

    [Fact]
    public void Capabilities_HideTheAspectRatioPicker_ButKeepAListWideEnoughToBeLeftAlone()
    {
        // Three things pull on this list at once, and only one of them is about aspect ratios:
        // the Output block hides on AspectRatio false, so nothing here is ever displayed;
        // GeneratorViewModel.RefreshCapabilities reads AspectRatios[0] before consulting that flag;
        // and the aspect-ratio Picker is two-way bound to Parameters.AspectRatio, so an ItemsSource
        // swap that drops the current value pushes null into it and persists that. Hence a broad
        // list: switching to a model that sends no ratio at all must not rewrite the user's saved
        // ratio to something else.
        var caps = _descriptor.Capabilities;

        caps.AspectRatio.Should().BeFalse();
        caps.AspectRatios.Should().Contain(["1:1", "16:9", "9:16"]);
    }
}
