using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;

namespace ImageGenerator.MAUI.Tests.Services.Civitai;

public class CivitaiMetaBuilderTests
{
    [Fact]
    public void Build_MapsCoreKeys()
    {
        var parameters = new ImageGenerationParameters
        {
            Prompt = "a red fox",
            Model = "openai/gpt-image-2",
            Seed = 1234567890123,
            AspectRatio = "3:2",
        };

        var meta = CivitaiMetaBuilder.Build(parameters);

        meta["prompt"].Should().Be("a red fox");
        // seed must stay numeric — CivitAI's imageMetaSchema coerces, but a number renders
        // correctly in the generation-data panel.
        meta["seed"].Should().Be(1234567890123L);
        // CivitAI's own gpt-image generations put the model name in `sampler`; mirror that.
        meta["sampler"].Should().Be("gpt-image-2");
        meta["Model"].Should().Be("openai/gpt-image-2");
        meta["Aspect ratio"].Should().Be("3:2");
    }

    [Fact]
    public void Build_ModelWithoutSlash_UsedVerbatimAsSampler()
    {
        var parameters = new ImageGenerationParameters { Prompt = "p", Model = "zimage", Seed = 1 };

        var meta = CivitaiMetaBuilder.Build(parameters);

        meta["sampler"].Should().Be("zimage");
        meta["Model"].Should().Be("zimage");
    }

    [Fact]
    public void Build_EmptyAspectRatio_OmitsKey()
    {
        var parameters = new ImageGenerationParameters { Prompt = "p", Model = "m", AspectRatio = "" };

        var meta = CivitaiMetaBuilder.Build(parameters);

        meta.Should().NotContainKey("Aspect ratio");
    }

    // ---- BuildFromFileMetadata (Gallery batch path) ----

    [Fact]
    public void BuildFromFileMetadata_MapsEmbeddedKeys()
    {
        var fileMeta = new Dictionary<string, string>
        {
            ["Prompt"] = "a red fox",
            ["ModelName"] = "openai/gpt-image-2",
            ["Seed"] = "1234567890123",
            ["AspectRatio"] = "3:2",
            ["Dimensions"] = "1536x1024", // ignored — not a CivitAI meta key
        };

        var meta = CivitaiMetaBuilder.BuildFromFileMetadata(fileMeta);

        meta.Should().NotBeNull();
        meta!["prompt"].Should().Be("a red fox");
        meta["seed"].Should().Be(1234567890123L, "a numeric seed is parsed to long");
        meta["sampler"].Should().Be("gpt-image-2");
        meta["Model"].Should().Be("openai/gpt-image-2");
        meta["Aspect ratio"].Should().Be("3:2");
    }

    [Fact]
    public void BuildFromFileMetadata_NonNumericSeed_KeptAsString()
    {
        var fileMeta = new Dictionary<string, string> { ["Prompt"] = "p", ["Seed"] = "auto" };

        var meta = CivitaiMetaBuilder.BuildFromFileMetadata(fileMeta);

        meta!["seed"].Should().Be("auto");
    }

    [Fact]
    public void BuildFromFileMetadata_NoPrompt_ReturnsNull()
    {
        var fileMeta = new Dictionary<string, string> { ["ModelName"] = "m", ["Seed"] = "1" };

        CivitaiMetaBuilder.BuildFromFileMetadata(fileMeta).Should().BeNull(
            "a bare image posts with no meta rather than an empty/prompt-less object");
    }

    [Fact]
    public void BuildFromFileMetadata_NullOrEmpty_ReturnsNull()
    {
        CivitaiMetaBuilder.BuildFromFileMetadata(null).Should().BeNull();
        CivitaiMetaBuilder.BuildFromFileMetadata(new Dictionary<string, string>()).Should().BeNull();
    }
}
