using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Descriptors;

/// <summary>
/// Pins the wire payload, capability declaration and metadata round-trip of the hosted gpt-image
/// models — and above all pins that each of them sends ONLY what its own schema declares.
///
/// The per-model field set is the point. These models share a name and not a vocabulary:
/// gpt-image-2 dropped <c>input_fidelity</c>, gpt-image-2.6-sunburst added <c>complexity</c> and
/// took aspect ratio from three values to eighteen. Sending a field a model does not declare is not
/// an error — cog silently drops it (replicate/cog#2943) — so the bug never surfaces as a failure,
/// only as a knob that does nothing. Sending an out-of-enum value on a declared field IS an error:
/// HTTP 422 on <c>input.aspect_ratio</c>, which is how this family first reached production broken.
/// </summary>
public class GptImageOnReplicateDescriptorTests
{
    private static readonly GptImage15Descriptor GptImage15 = new();
    private static readonly GptImage2Descriptor GptImage2 = new();
    private static readonly GptImage26SunburstDescriptor GptImage26Sunburst = new();

    private readonly IModelDescriptorRegistry _registry = ModelDescriptorRegistry.Default();

    private static IDictionary<string, object?> Build(
        GptImageOnReplicateDescriptor descriptor, ImageGenerationParameters p) =>
        (IDictionary<string, object?>)descriptor.Build(p);

    private static ImageGenerationParameters Params(
        GptImageOnReplicateDescriptor descriptor,
        ImageOutputFormat? format = null,
        int? outputQuality = null)
    {
        var p = new ImageGenerationParameters
        {
            Model = descriptor.ModelId,
            Prompt = "a cat",
            AspectRatio = "1:1"
        };
        if (format is { } f) p.OutputFormat = f;
        if (outputQuality is { } q) p.OutputQuality = q;
        return p;
    }

    private static IReadOnlyDictionary<string, string> ParseLines(IEnumerable<string> lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var value = line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            dict[line[..colon]] = value;
        }
        return dict;
    }

    // ---------------------------------------------------------------- the per-model field set

    /// <summary>
    /// The exact key set each model may put on the wire. A member order assertion would be wrong —
    /// Replicate resolves input fields by name — but "exactly these keys, nothing else" is the
    /// whole contract, and it is per model on purpose.
    /// </summary>
    [Theory]
    [MemberData(nameof(VocabularyCases))]
    public void Build_SendsExactlyWhatThatModelDeclares(
        GptImageOnReplicateDescriptor descriptor, string[] expected)
    {
        Build(descriptor, Params(descriptor)).Keys.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void GptImage15_IsTheOnlyOneOfTheThreeDeclaringInputFidelity()
    {
        Build(GptImage15, Params(GptImage15))["input_fidelity"].Should().Be("low");
        Build(GptImage2, Params(GptImage2)).Should().NotContainKey("input_fidelity");
        Build(GptImage26Sunburst, Params(GptImage26Sunburst)).Should().NotContainKey("input_fidelity");
    }

    [Fact]
    public void GptImage26Sunburst_IsTheOnlyOneOfTheThreeDeclaringComplexity()
    {
        // Presence is the whole contract here; the value is whatever the app defaults the knob to.
        Build(GptImage26Sunburst, Params(GptImage26Sunburst)).Should().ContainKey("complexity");
        Build(GptImage15, Params(GptImage15)).Should().NotContainKey("complexity");
        Build(GptImage2, Params(GptImage2)).Should().NotContainKey("complexity");
    }

    [Theory]
    [MemberData(nameof(HostedDescriptors))]
    public void Build_NeverSendsSeedOrNumberOfImages(GptImageOnReplicateDescriptor descriptor)
    {
        // Neither field exists in any hosted gpt-image schema, and cog silently drops unknown
        // inputs — a stray key here is a knob doing nothing, not an error. Keep them out.
        Build(descriptor, Params(descriptor)).Should().NotContainKeys("seed", "number_of_images");
    }

    // ---------------------------------------------------------------- the shared payload shape

    [Theory]
    [MemberData(nameof(HostedDescriptors))]
    public void Build_MapsJpgToJpegAndCarriesCompressionIntoOutputCompression(
        GptImageOnReplicateDescriptor descriptor)
    {
        var payload = Build(descriptor, Params(descriptor, ImageOutputFormat.Jpg, 42));

        payload["output_format"].Should().Be("jpeg");
        payload["output_compression"].Should().Be(42);
    }

    [Theory]
    [MemberData(nameof(HostedDescriptors))]
    public void Build_AttachmentsGoToInputImages_NeverToImages(GptImageOnReplicateDescriptor descriptor)
    {
        // The generic Replicate fallback sends "images"; on this family that name is simply
        // dropped server-side, i.e. the attached image silently never arrives.
        var p = Params(descriptor);
        p.ImagePrompts.Add("test-base64-data");

        var payload = Build(descriptor, p);

        payload.Should().NotContainKey("images");
        ((string[])payload["input_images"]!).Should().Equal("data:image/png;base64,test-base64-data");
    }

    [Theory]
    [MemberData(nameof(HostedDescriptors))]
    public void Build_WithoutAttachments_LeavesInputImagesNull(GptImageOnReplicateDescriptor descriptor)
    {
        Build(descriptor, Params(descriptor))["input_images"].Should().BeNull();
    }

    // ---------------------------------------------------------------- capabilities

    [Theory]
    [MemberData(nameof(HostedDescriptors))]
    public void Capabilities_AreGptImageShaped(GptImageOnReplicateDescriptor descriptor)
    {
        var caps = descriptor.Capabilities;

        caps.Seed.Should().BeFalse();
        caps.SafetyTolerance.Should().BeFalse();
        caps.PromptUpsampling.Should().BeFalse();
        caps.CustomDimensions.Should().BeFalse();
        caps.ImagePrompt.Should().BeTrue();
        caps.MaxImageInputs.Should().Be(10);
        caps.AspectRatio.Should().BeTrue();
        caps.AspectRatioLabel.Should().Be("Aspect ratio");
        // RefreshCapabilities falls back to AspectRatios[0] when the current value is not offered,
        // so the first entry doubles as the model's own server default. Both hold while it is "1:1".
        caps.AspectRatios[0].Should().Be("1:1");
        caps.GptQualityOptions!.Should().Contain("auto").And.HaveCountGreaterThanOrEqualTo(4);
        caps.GptBackgroundOptions!.Should().BeEquivalentTo(["auto", "transparent", "opaque"]);
        caps.GptModerationOptions!.Should().BeEquivalentTo(["auto", "low"]);
    }

    [Fact]
    public void GptImage26Sunburst_Capabilities_MatchItsSchema()
    {
        var caps = GptImage26Sunburst.Capabilities;

        // Every value the 422 enumerated, in the schema's own order.
        caps.AspectRatios.Should().BeEquivalentTo(
            ["1:1", "3:2", "2:3", "4:3", "3:4", "16:9", "9:16", "auto",
             "1024x1024", "1536x1024", "1024x1536", "1536x1152", "1152x1536",
             "2048x2048", "2048x1152", "1152x2048", "3840x2160", "2160x3840"],
            opts => opts.WithStrictOrdering());
        caps.GptQualityOptions.Should().BeEquivalentTo(["auto", "low", "medium", "high", "xhigh", "max"]);
        caps.GptComplexityOptions.Should().BeEquivalentTo(["low", "medium", "high"]);
        caps.GptInputFidelityOptions.Should().BeNull();
    }

    [Fact]
    public void GptImage15_And_2_KeepTheirThreeAspectRatios()
    {
        // The 2.6 schema widening must not leak backwards: offering 4:5 on gpt-image-1.5 is the
        // same 422 the fallback was producing.
        GptImage15.Capabilities.AspectRatios.Should().BeEquivalentTo(["1:1", "3:2", "2:3"]);
        GptImage2.Capabilities.AspectRatios.Should().BeEquivalentTo(["1:1", "3:2", "2:3"]);
        GptImage15.Capabilities.GptComplexityOptions.Should().BeNull();
        GptImage2.Capabilities.GptComplexityOptions.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(HostedDescriptors))]
    public void Capabilities_ExposedOptions_AreExactlyWhatBuildWillAccept(
        GptImageOnReplicateDescriptor descriptor)
    {
        // The pickers offer these lists and nothing else can change the parameter, so a value
        // outside a list can only arrive from a restored image's metadata. Whatever the lists
        // contain must be a superset of what the payload ever carries.
        var caps = descriptor.Capabilities;
        var payload = Build(descriptor, Params(descriptor));

        caps.AspectRatios.Should().Contain((string)payload["aspect_ratio"]!);
        caps.GptQualityOptions!.Should().Contain((string)payload["quality"]!);
        caps.GptBackgroundOptions!.Should().Contain((string)payload["background"]!);
        caps.GptModerationOptions!.Should().Contain((string)payload["moderation"]!);
        if (caps.GptComplexityOptions is { } complexity)
            complexity.Should().Contain((string)payload["complexity"]!);
    }

    // ---------------------------------------------------------------- metadata round-trip

    [Theory]
    [MemberData(nameof(HostedDescriptors))]
    public void Metadata_RoundTripsTheSharedGptKnobs(GptImageOnReplicateDescriptor descriptor)
    {
        var source = Params(descriptor);
        source.GptQuality = "high";
        source.GptBackground = "transparent";
        source.GptModeration = "low";

        var fresh = new ImageGenerationParameters { Model = descriptor.ModelId };
        descriptor.Apply(fresh, ParseLines(descriptor.Lines(source)));

        fresh.GptQuality.Should().Be("high");
        fresh.GptBackground.Should().Be("transparent");
        fresh.GptModeration.Should().Be("low");
    }

    [Fact]
    public void Metadata_ComplexityRoundTripsOnTheModelThatHasIt_AndIsSilentOnTheOthers()
    {
        var source = Params(GptImage26Sunburst);
        source.GptComplexity = "low";

        var lines = ParseLines(GptImage26Sunburst.Lines(source));
        lines.Should().ContainKey("GptComplexity");

        var fresh = new ImageGenerationParameters { Model = GptImage26Sunburst.ModelId, GptComplexity = "high" };
        GptImage26Sunburst.Apply(fresh, lines);
        fresh.GptComplexity.Should().Be("low");

        // A model without the field writes no line and adopts no value — otherwise a gpt-image-1.5
        // PNG would start carrying a field its schema never declared.
        GptImage15.Lines(Params(GptImage15)).Should().NotContain(l => l.StartsWith("GptComplexity:"));
        var untouched = new ImageGenerationParameters { Model = GptImage15.ModelId, GptComplexity = "high" };
        GptImage15.Apply(untouched, new Dictionary<string, string> { ["GptComplexity"] = "low" });
        untouched.GptComplexity.Should().Be("high");
    }

    [Fact]
    public void Metadata_InputFidelityLineOnlyExistsWhereTheFieldDoes()
    {
        GptImage15.Lines(Params(GptImage15)).Should().Contain(l => l.StartsWith("GptInputFidelity:"));
        GptImage2.Lines(Params(GptImage2)).Should().NotContain(l => l.StartsWith("GptInputFidelity:"));
        GptImage26Sunburst.Lines(Params(GptImage26Sunburst))
            .Should().NotContain(l => l.StartsWith("GptInputFidelity:"));
    }

    [Theory]
    [MemberData(nameof(HostedDescriptors))]
    public void Apply_RejectsValuesTheModelDoesNotOffer(GptImageOnReplicateDescriptor descriptor)
    {
        var p = Params(descriptor);
        var before = p.GptQuality;

        descriptor.Apply(p, new Dictionary<string, string> { ["GptQuality"] = "ultra" });

        p.GptQuality.Should().Be(before);
    }

    [Fact]
    public void Apply_DoesNotAdoptAnAspectRatioTheModelNeverOffered()
    {
        // "1024x1024" is legal on 2.6 and illegal on 1.5. Aspect ratio is not part of the descriptor
        // metadata round-trip (the VM gates it on Capabilities.AspectRatios), so pin that it stays
        // out of Apply entirely rather than being adopted unconditionally.
        var p = Params(GptImage15);
        var before = p.AspectRatio;

        GptImage15.Apply(p, new Dictionary<string, string> { ["AspectRatio"] = "1024x1024" });

        p.AspectRatio.Should().Be(before);
    }

    // ---------------------------------------------------------------- registration

    [Fact]
    public void Registry_ResolvesEveryHostedModel_NotTheFallback()
    {
        _registry.PayloadFor(ModelConstants.OpenAI.GptImage15OnReplicate).ModelId
            .Should().Be(ModelConstants.OpenAI.GptImage15OnReplicate);
        _registry.PayloadFor(ModelConstants.OpenAI.GptImage2OnReplicate).ModelId
            .Should().Be(ModelConstants.OpenAI.GptImage2OnReplicate);
        // An id the catalog returns but nobody registered would fall through to
        // FallbackReplicateDescriptor, which cannot drive this family.
        _registry.PayloadFor(ModelConstants.OpenAI.GptImage26SunburstOnReplicate).ModelId
            .Should().Be(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);
        _registry.CapabilitiesFor(ModelConstants.OpenAI.GptImage26SunburstOnReplicate).ModelId
            .Should().Be(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);
        _registry.MetadataFor(ModelConstants.OpenAI.GptImage26SunburstOnReplicate)
            .Should().BeOfType<GptImage26SunburstDescriptor>();
    }

    [Fact]
    public void Seeds_AreSeededUnderReplicateWithDisplayNames()
    {
        new GptImage15Descriptor().Seed.Should().BeEquivalentTo(
            new { Display = "GPT Image 1.5", Value = ModelConstants.OpenAI.GptImage15OnReplicate,
                  Provider = ProviderConstants.Replicate });
        new GptImage2Descriptor().Seed.Should().BeEquivalentTo(
            new { Display = "GPT Image 2", Value = ModelConstants.OpenAI.GptImage2OnReplicate,
                  Provider = ProviderConstants.Replicate });
        new GptImage26SunburstDescriptor().Seed.Should().BeEquivalentTo(
            new { Display = "GPT Image 2.6 Sunburst",
                  Value = ModelConstants.OpenAI.GptImage26SunburstOnReplicate,
                  Provider = ProviderConstants.Replicate });

        _registry.Seeds.Select(s => s.Value).Should()
            .Contain(ModelConstants.OpenAI.GptImage15OnReplicate)
            .And.Contain(ModelConstants.OpenAI.GptImage2OnReplicate)
            .And.Contain(ModelConstants.OpenAI.GptImage26SunburstOnReplicate);
    }

    public static TheoryData<GptImageOnReplicateDescriptor> HostedDescriptors =>
    [
        GptImage15,
        GptImage2,
        GptImage26Sunburst
    ];

    public static TheoryData<GptImageOnReplicateDescriptor, string[]> VocabularyCases => new()
    {
        { GptImage15, ["prompt", "aspect_ratio", "output_format", "output_compression",
                      "quality", "background", "moderation", "input_fidelity", "input_images"] },
        // No input_fidelity: documents/gpt-image-2_input_schema.txt does not declare it.
        { GptImage2, ["prompt", "aspect_ratio", "output_format", "output_compression",
                     "quality", "background", "moderation", "input_images"] },
        // No input_fidelity, plus complexity: documents/gpt-image-2.6-sunburst_input_schema.txt.
        { GptImage26Sunburst, ["prompt", "aspect_ratio", "output_format", "output_compression",
                              "quality", "background", "moderation", "input_images", "complexity"] },
    };
}
