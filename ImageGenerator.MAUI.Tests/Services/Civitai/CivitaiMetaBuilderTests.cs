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
}
