using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Descriptors;

public class IdeogramV4DescriptorTests
{
    private readonly IModelDescriptorRegistry _registry = ModelDescriptorRegistry.Default();

    private static readonly IdeogramV4Descriptor[] All =
    [
        new IdeogramV4BalancedDescriptor(),
        new IdeogramV4TurboDescriptor(),
        new IdeogramV4QualityDescriptor()
    ];

    [Fact]
    public void Build_SendsPromptOnly()
    {
        var descriptor = new IdeogramV4QualityDescriptor();

        var payload = (IDictionary<string, object?>)descriptor.Build(
            new ImageGenerationParameters { Model = descriptor.ModelId, Prompt = "a cat", Raw = false });

        payload["prompt"].Should().Be("a cat");
        // Ideogram V4's schema has none of the fallback's extra fields (aspect_ratio / seed /
        // output_format / output_quality / images) — sending them would 422. Only prompt ships.
        payload.Keys.Should().BeEquivalentTo(["prompt"]);
    }

    [Fact]
    public void Seeds_AreGroupedUnderReplicateProvider()
    {
        foreach (var descriptor in All)
        {
            descriptor.Seed.Provider.Should().Be(ProviderConstants.Replicate);
        }
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
        var builder = _registry.PayloadFor(ModelConstants.Ideogram.V4Quality);

        // The fallback declares the sentinel id "_fallback"; the dedicated descriptor declares
        // the real model id, proving the registry routes Ideogram to the correct payload builder.
        builder.ModelId.Should().Be(ModelConstants.Ideogram.V4Quality);
    }

    [Fact]
    public void Capabilities_HideEveryKnob_ButKeepAspectRatiosNonEmpty()
    {
        var caps = new IdeogramV4QualityDescriptor().Capabilities;

        caps.AspectRatio.Should().BeFalse();
        caps.Seed.Should().BeFalse();
        caps.OutputQuality.Should().BeFalse();
        caps.ImagePrompt.Should().BeFalse();
        caps.MaxImageInputs.Should().Be(0);
        caps.Resolutions.Should().BeNull();
        // RefreshCapabilities indexes AspectRatios[0] unconditionally, so it must stay non-empty
        // even though the AspectRatio picker is hidden.
        caps.AspectRatios.Should().NotBeEmpty();
    }
}
