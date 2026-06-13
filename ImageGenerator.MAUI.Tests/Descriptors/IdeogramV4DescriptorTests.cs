using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Descriptors;

public class IdeogramV4DescriptorTests
{
    private readonly IModelDescriptorRegistry _registry = ModelDescriptorRegistry.Default();
    private readonly IdeogramV4QualityDescriptor _descriptor = new();

    private static readonly IdeogramV4Descriptor[] All =
    [
        new IdeogramV4BalancedDescriptor(),
        new IdeogramV4TurboDescriptor(),
        new IdeogramV4QualityDescriptor()
    ];

    private static ImageGenerationParameters Params(
        string prompt = "a cat",
        bool useJson = false,
        string resolution = IdeogramV4Descriptor.AutoResolution,
        bool copyright = false) =>
        new()
        {
            Model = ModelConstants.Ideogram.V4Quality,
            Prompt = prompt,
            UseJsonPrompt = useJson,
            Resolution = resolution,
            EnableCopyrightDetection = copyright,
            Raw = false
        };

    private static IDictionary<string, object?> Build(IdeogramV4Descriptor d, ImageGenerationParameters p) =>
        (IDictionary<string, object?>)d.Build(p);

    [Fact]
    public void Build_PlainPrompt_Auto_NoCopyright_SendsPromptOnly()
    {
        var payload = Build(_descriptor, Params());

        payload["prompt"].Should().Be("a cat");
        // None of the fallback's extra fields, and resolution/copyright omitted at their defaults.
        payload.Keys.Should().BeEquivalentTo(["prompt"]);
    }

    [Fact]
    public void Build_WithExplicitResolution_IncludesIt()
    {
        Build(_descriptor, Params(resolution: "2048x2048"))["resolution"].Should().Be("2048x2048");
    }

    [Fact]
    public void Build_AutoResolution_OmitsResolution()
    {
        Build(_descriptor, Params(resolution: IdeogramV4Descriptor.AutoResolution))
            .Should().NotContainKey("resolution");
    }

    [Fact]
    public void Build_JsonMode_SendsJsonPromptAsString_NotPrompt()
    {
        // Replicate's cog types json_prompt as a string, so the raw box text ships verbatim.
        var json = """{"high_level_description":"a cat"}""";
        var payload = Build(_descriptor, Params(prompt: json, useJson: true));

        payload.Should().NotContainKey("prompt");
        payload["json_prompt"].Should().Be(json);
    }

    [Fact]
    public void Build_CopyrightDetection_IncludedOnlyWhenEnabled()
    {
        Build(_descriptor, Params(copyright: false)).Should().NotContainKey("enable_copyright_detection");
        Build(_descriptor, Params(copyright: true))["enable_copyright_detection"].Should().Be(true);
    }

    [Fact]
    public void Seeds_AreGroupedUnderReplicateProvider()
    {
        foreach (var descriptor in All)
            descriptor.Seed.Provider.Should().Be(ProviderConstants.Replicate);
    }

    [Fact]
    public void Seeds_ExposeExpectedModelIdsAndDisplayNames()
    {
        new IdeogramV4BalancedDescriptor().Seed.Should()
            .BeEquivalentTo(new { Display = "Ideogram V4 Balanced", Value = ModelConstants.Ideogram.V4Balanced });
        new IdeogramV4TurboDescriptor().Seed.Should()
            .BeEquivalentTo(new { Display = "Ideogram V4 Turbo", Value = ModelConstants.Ideogram.V4Turbo });
        new IdeogramV4QualityDescriptor().Seed.Should()
            .BeEquivalentTo(new { Display = "Ideogram V4 Quality", Value = ModelConstants.Ideogram.V4Quality });
    }

    [Fact]
    public void Registry_PayloadFor_V4Quality_ReturnsDedicatedDescriptor_NotFallback()
    {
        // The fallback declares the sentinel id "_fallback"; a real id proves correct routing.
        _registry.PayloadFor(ModelConstants.Ideogram.V4Quality).ModelId
            .Should().Be(ModelConstants.Ideogram.V4Quality);
    }

    [Fact]
    public void Apply_RoundTripsResolutionJsonPromptAndCopyright()
    {
        var source = Params(useJson: true, resolution: "2048x2048", copyright: true);

        var fresh = new ImageGenerationParameters();
        _descriptor.Apply(fresh, ParseLines(_descriptor.Lines(source)));

        fresh.Resolution.Should().Be("2048x2048");
        fresh.UseJsonPrompt.Should().BeTrue();
        fresh.EnableCopyrightDetection.Should().BeTrue();
    }

    [Fact]
    public void Apply_ResolutionNotInIdeogramSet_IsIgnored()
    {
        var p = new ImageGenerationParameters { Resolution = IdeogramV4Descriptor.AutoResolution };

        _descriptor.Apply(p, new Dictionary<string, string> { ["Resolution"] = "1K" });

        p.Resolution.Should().Be(IdeogramV4Descriptor.AutoResolution);
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

    [Fact]
    public void Capabilities_AreIdeogramShaped()
    {
        var caps = _descriptor.Capabilities;

        caps.IdeogramOptions.Should().BeTrue();
        caps.OutputFormatSelectable.Should().BeFalse();   // PNG-only model
        caps.AspectRatio.Should().BeFalse();
        caps.Seed.Should().BeFalse();
        caps.OutputQuality.Should().BeFalse();
        caps.ImagePrompt.Should().BeFalse();
        caps.MaxImageInputs.Should().Be(0);
        caps.Resolutions.Should().NotBeNull();
        caps.Resolutions![0].Should().Be(IdeogramV4Descriptor.AutoResolution);
        caps.Resolutions.Should().Contain("2048x2048");
        // RefreshCapabilities indexes AspectRatios[0] unconditionally even when AspectRatio is off.
        caps.AspectRatios.Should().NotBeEmpty();
    }
}
