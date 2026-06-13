using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Pollinations;
using ImageGenerator.MAUI.Core.Domain.Entities;

namespace ImageGenerator.MAUI.Tests.Descriptors;

/// <summary>
/// Round-trip coverage for IMetadataDescriber.Apply — the inverse of Lines used by "Remix from an
/// image". For each descriptor: write Lines from known parameters, parse them back the way
/// GalleryService does, Apply onto a fresh parameters object, and assert the values survived. This
/// is the guard against the write/read pair drifting.
/// </summary>
public class MetadataDescriberApplyTests
{
    // Mirrors GalleryService.ParseLines: split on the first colon, drop one leading space.
    private static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon];
            var value = line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            dict[key] = value;
        }
        return dict;
    }

    private static ImageGenerationParameters RoundTrip(IMetadataDescriber d, ImageGenerationParameters source)
    {
        var fresh = new ImageGenerationParameters();
        d.Apply(fresh, Parse(d.Lines(source)));
        return fresh;
    }

    [Fact]
    public void Flux11Pro_RoundTripsUpsampling()
    {
        var d = new Flux11ProDescriptor();
        RoundTrip(d, new ImageGenerationParameters { PromptUpsampling = true })
            .PromptUpsampling.Should().BeTrue();
    }

    [Fact]
    public void Flux11ProUltra_RoundTripsRawAndImagePromptStrength()
    {
        var d = new Flux11ProUltraDescriptor();
        var result = RoundTrip(d, new ImageGenerationParameters { Raw = true, ImagePromptStrength = 0.33 });

        result.Raw.Should().BeTrue();
        result.ImagePromptStrength.Should().Be(0.33);
    }

    [Fact]
    public void GptImage_RoundTripsAllFourKnobs()
    {
        var d = new GptImage15Descriptor();
        var result = RoundTrip(d, new ImageGenerationParameters
        {
            GptQuality = "high",
            GptBackground = "opaque",
            GptModeration = "low",
            GptInputFidelity = "high"
        });

        result.GptQuality.Should().Be("high");
        result.GptBackground.Should().Be("opaque");
        result.GptModeration.Should().Be("low");
        result.GptInputFidelity.Should().Be("high");
    }

    [Fact]
    public void NanoBanana2_RoundTripsResolution()
    {
        var d = new NanoBanana2Descriptor();
        RoundTrip(d, new ImageGenerationParameters { Resolution = "4K" })
            .Resolution.Should().Be("4K");
    }

    [Fact]
    public void Pollinations_RoundTripsSafe()
    {
        var d = new PollinationsFluxDescriptor();
        RoundTrip(d, new ImageGenerationParameters { Safe = true })
            .Safe.Should().BeTrue();
    }

    // ---- defensive parsing ------------------------------------------------------------------

    [Fact]
    public void Apply_MissingKey_LeavesExistingValueUntouched()
    {
        var d = new Flux11ProUltraDescriptor();
        var p = new ImageGenerationParameters { Raw = true, ImagePromptStrength = 0.7 };

        d.Apply(p, new Dictionary<string, string>());

        p.Raw.Should().BeTrue();
        p.ImagePromptStrength.Should().Be(0.7);
    }

    [Fact]
    public void Apply_UnparseableBool_IsIgnored()
    {
        var d = new Flux11ProDescriptor();
        var p = new ImageGenerationParameters { PromptUpsampling = false };

        d.Apply(p, new Dictionary<string, string> { ["Upsampling"] = "notabool" });

        p.PromptUpsampling.Should().BeFalse();
    }

    [Fact]
    public void Apply_DoubleIsParsedInvariant()
    {
        var d = new Flux11ProUltraDescriptor();
        var p = new ImageGenerationParameters();

        d.Apply(p, new Dictionary<string, string> { ["ImagePromptStrength"] = "0.5" });

        p.ImagePromptStrength.Should().Be(0.5);
    }

    [Fact]
    public void Apply_GptValueNotInOptionSet_IsIgnored()
    {
        var d = new GptImage15Descriptor();
        var p = new ImageGenerationParameters { GptQuality = "auto" };

        d.Apply(p, new Dictionary<string, string> { ["GptQuality"] = "ultra" });

        p.GptQuality.Should().Be("auto");
    }

    [Fact]
    public void Apply_NanoBananaResolutionNotInOptionSet_IsIgnored()
    {
        var d = new NanoBanana2Descriptor();
        var p = new ImageGenerationParameters { Resolution = "1K" };

        d.Apply(p, new Dictionary<string, string> { ["Resolution"] = "8K" });

        p.Resolution.Should().Be("1K");
    }
}
