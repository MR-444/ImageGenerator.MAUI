using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Entities;

/// <summary>
/// What a fresh <see cref="ImageGenerationParameters"/> holds — the values behind every control the
/// user has not touched — and where the app leaves the three cost knobs in particular. Those do not
/// follow the model's own schema defaults on purpose: those are the model owner's quality choice
/// ("complexity" "high", "quality" "auto" — and "auto" is free to resolve to "max"), paid by
/// whoever generates.
///
/// The vocabulary check is the part that could rot silently. The VM snaps each knob to a value the
/// selected model actually declares, so a default that no schema contains is not an error anywhere —
/// it quietly becomes whatever the first offered option is, and the cheap setting the app promised
/// stops being sent without a single test failing.
/// </summary>
public class ImageGenerationParametersDefaultsTests
{
    [Fact]
    public void Defaults_SitAtTheCheapEndOfTheCostKnobs()
    {
        var p = new ImageGenerationParameters();

        p.GptQuality.Should().Be("medium");
        p.GptModeration.Should().Be("low");
        p.GptComplexity.Should().Be("low");
    }

    /// <summary>
    /// The rest of what a fresh run looks like. This object used to be started twice: the entity
    /// initialized these fields and GeneratorViewModel's constructor overwrote them with its own
    /// copy — ten lines of restated default, one of them contradicting the entity (half the width
    /// slider is 1024×1024, which is not 16:9, while the aspect-ratio default is 16:9). This is the
    /// assertion that fails when someone puts the second copy back.
    /// </summary>
    [Fact]
    public void FreshParameters_StartAtTheShapeTheAppWasBuiltAround()
    {
        var p = new ImageGenerationParameters();

        p.Model.Should().Be(ModelConstants.OpenAI.GptImage15OnReplicate);
        p.AspectRatio.Should().Be("16:9");
        p.Width.Should().Be(1920);
        p.Height.Should().Be(1080);
        p.OutputFormat.Should().Be(ImageOutputFormat.Png);
        p.RandomizeSeed.Should().BeTrue();
        p.PromptUpsampling.Should().BeFalse();
        // Stated as the intent, not the number: the app starts at the top of the allowed range for
        // both, and those ranges are what ValidationConstants owns.
        p.OutputQuality.Should().Be(ValidationConstants.OutputQualityMax);
        p.SafetyTolerance.Should().Be(ValidationConstants.SafetyMax);
        // Not 0 or a number of its own: the seed the session starts on is random per launch and
        // regenerated per run, which is a session fact and not a default.
        p.Seed.Should().Be(0);

        // The pair has to mean the ratio it names, or the first custom-dimension model opens on a
        // picture that is not the aspect ratio the rest of the UI says was chosen.
        ((double)p.Width / p.Height).Should().Be(16.0 / 9.0);
    }

    [Theory]
    [MemberData(nameof(OfferedVocabularies))]
    public void Default_IsAValueSomeModelActuallyDeclares(string descriptor, string knob, string theDefault, string[] offered)
    {
        offered.Should().Contain(theDefault,
            $"{descriptor} offers {knob} without its app default, so selecting it snaps the knob " +
            "to another value and the cheap setting quietly stops being sent");
    }

    public static TheoryData<string, string, string, string[]> OfferedVocabularies
    {
        get
        {
            var defaults = new ImageGenerationParameters();
            var data = new TheoryData<string, string, string, string[]>();
            foreach (var descriptor in ModelDescriptorRegistry.ProductionDescriptors().OfType<ICapabilityProvider>())
            {
                var caps = descriptor.Capabilities;
                Add(data, descriptor.GetType().Name, "quality", defaults.GptQuality, caps.GptQualityOptions);
                Add(data, descriptor.GetType().Name, "moderation", defaults.GptModeration, caps.GptModerationOptions);
                Add(data, descriptor.GetType().Name, "complexity", defaults.GptComplexity, caps.GptComplexityOptions);
            }

            return data;
        }
    }

    private static void Add(
        TheoryData<string, string, string, string[]> data,
        string descriptor,
        string knob,
        string theDefault,
        IReadOnlyList<string>? options)
    {
        if (options is not null) data.Add(descriptor, knob, theDefault, options.ToArray());
    }
}
