using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Descriptors;

public class ModelDescriptorRegistryTests
{
    private readonly IModelDescriptorRegistry _registry = ModelDescriptorRegistry.Default();

    [Fact]
    public void PayloadFor_UnknownReplicatePath_Throws()
    {
        var act = () => _registry.PayloadFor("stability-ai/some-new-model");

        // There is no fallback payload any more: this build does not build requests for models it
        // cannot describe. ReplicateImageGenerationService refuses them earlier so nothing is
        // billed; this is the same rule for a caller that forgot it.
        act.Should().Throw<ArgumentException>().WithMessage("*stability-ai/some-new-model*");
    }

    [Fact]
    public void PayloadFor_NotALikelyReplicatePath_Throws()
    {
        var act = () => _registry.PayloadFor("not-a-path");

        act.Should().Throw<ArgumentException>().WithMessage("*not-a-path*");
    }

    [Fact]
    public void CapabilitiesFor_UnknownModel_ReturnsFallback()
    {
        var caps = _registry.CapabilitiesFor("stability-ai/whatever").Capabilities;

        // Conservative defaults from FallbackReplicateDescriptor: nothing is offered that this build
        // could not send — which, for a model with no descriptor, is nothing at all. Detail lives in
        // FallbackReplicateDescriptorTests.
        caps.SafetyTolerance.Should().BeFalse();
        caps.PromptUpsampling.Should().BeFalse();
        caps.Seed.Should().BeFalse("no seed is sent, so the field would be decorative");
        caps.AspectRatio.Should().BeFalse("no aspect_ratio is sent");
        caps.MaxImageInputs.Should().Be(0);
        caps.ImagePrompt.Should().BeFalse("accepting a reference image promises an edit this build cannot make");
    }

    [Fact]
    public void MetadataFor_UnknownModel_ReturnsNull()
    {
        _registry.MetadataFor("stability-ai/anything").Should().BeNull();
    }

    [Fact]
    public void MetadataFor_FluxProUltra_DoesNotIncludeUpsamplingLine()
    {
        // Regression for the substring-match bug fixed in commit 009e5a1 — at the descriptor
        // level we now have separate classes per id, so this is structurally impossible, but
        // the test pins the property in case a future descriptor split goes wrong.
        var lines = _registry.MetadataFor(ModelConstants.Flux.Pro11Ultra)!
            .Lines(new ImageGenerationParameters { Model = ModelConstants.Flux.Pro11Ultra, Prompt = "x", Raw = false })
            .ToList();

        lines.Should().NotContain(l => l.StartsWith("Upsampling:"));
        lines.Should().Contain(l => l.StartsWith("Raw:"));
    }

    [Fact]
    public void Seeds_ContainsExpectedFirstLaunchEntries()
    {
        var seedIds = _registry.Seeds.Select(s => s.Value).ToList();

        seedIds.Should().Contain(ModelConstants.OpenAI.GptImage15OnReplicate);
        seedIds.Should().Contain(ModelConstants.OpenAI.GptImage2OnReplicate);
        seedIds.Should().Contain(ModelConstants.Flux.Pro11);
        seedIds.Should().Contain(ModelConstants.Flux.Pro11Ultra);
        seedIds.Should().Contain(ModelConstants.Flux.Klein4b);
        seedIds.Should().Contain(ModelConstants.Google.NanoBanana2);
        seedIds.Should().Contain(ModelConstants.Ideogram.V4Balanced);
        seedIds.Should().Contain(ModelConstants.Ideogram.V4Turbo);
        seedIds.Should().Contain(ModelConstants.Ideogram.V4Quality);
        seedIds.Should().Contain(ModelConstants.Pollinations.Flux);
        seedIds.Should().Contain(ModelConstants.Pollinations.Zimage);
        seedIds.Should().Contain(ModelConstants.Pollinations.QwenImage);
        // Flex/Pro/Max are NOT seeded — they only appear after Refresh Models hydrates.
        seedIds.Should().NotContain(ModelConstants.Flux.Flex2);
        seedIds.Should().NotContain(ModelConstants.Flux.Pro2);
        seedIds.Should().NotContain(ModelConstants.Flux.Max2);
    }

    [Theory]
    [InlineData(ModelConstants.Pollinations.GptImage)]
    [InlineData(ModelConstants.Pollinations.GptImageLarge)]
    [InlineData(ModelConstants.Pollinations.GptImage2)]
    public void PollinationsGptImageModels_ExposeQualityOptions_MediumFirst(string modelId)
    {
        // Drives SupportsGptQuality → the quality Picker in MainPage. Pollinations' enum has no
        // "auto" (unlike the same models on Replicate), and medium leads so that RefreshCapabilities'
        // snap-to-first lands on the cost tier the user wants rather than a high-priced auto.
        var caps = _registry.CapabilitiesFor(modelId).Capabilities;

        caps.GptQualityOptions.Should().Equal("medium", "low", "high", "hd");
    }

    [Fact]
    public void PollinationsNonGptImageModels_ExposeNoQualityOptions()
    {
        _registry.CapabilitiesFor(ModelConstants.Pollinations.Flux)
            .Capabilities.GptQualityOptions.Should().BeNull();
    }

    [Fact]
    public void Seeds_CountMatchesProductionDescriptorSet()
    {
        // 6 originals (GPT 1.5, GPT 2, Flux Pro, Flux Pro Ultra, Klein4b, NanoBanana2)
        // + 3 pinned Ideogram V4 (balanced/turbo/quality)
        // + 3 Pollinations (flux/zimage/qwen-image)
        // + 3 Pollinations gptimage family, seeded for their `quality` capability
        // + GPT Image 2.6 Sunburst (registered because its schema differs from the other two
        //   hosted gpt-image models, and seeded so it is usable before the first Refresh) = 16.
        _registry.Seeds.Should().HaveCount(16);
    }

    [Fact]
    public void Default_RegistersPollinationsDescriptors_NotJustTheFallback()
    {
        // Default() must mirror MauiProgram's DI set: the typed Pollinations descriptors
        // (audit finding — they were missing, so registry tests ran a different production
        // set than the app shipped).
        _registry.PayloadFor(ModelConstants.Pollinations.Flux).ModelId.Should().Be(ModelConstants.Pollinations.Flux);
        _registry.PayloadFor(ModelConstants.Pollinations.Zimage).ModelId.Should().Be(ModelConstants.Pollinations.Zimage);
        _registry.PayloadFor(ModelConstants.Pollinations.QwenImage).ModelId.Should().Be(ModelConstants.Pollinations.QwenImage);
        _registry.CapabilitiesFor(ModelConstants.Pollinations.Flux).ModelId.Should().Be(ModelConstants.Pollinations.Flux);
    }

    [Fact]
    public void PayloadFor_ComfyUiId_RoutesToComfyFallback_NotReplicate()
    {
        // "comfyui/foo" satisfies the owner/name heuristic — the explicit branch must win,
        // including for workflow stems with spaces.
        _registry.PayloadFor("comfyui/Ideogram workflow_MR").ModelId.Should().Be("_fallback_comfyui");
        _registry.PayloadFor("comfyui/x").ModelId.Should().Be("_fallback_comfyui");
    }

    [Fact]
    public void CapabilitiesFor_ComfyUiId_DeclaresSeedJsonEditorAndResolutionKnobs()
    {
        var caps = _registry.CapabilitiesFor("comfyui/anything").Capabilities;

        caps.Seed.Should().BeTrue();
        caps.JsonPromptEditor.Should().BeTrue();
        caps.IdeogramOptions.Should().BeFalse("the Replicate resolution/copyright block must stay hidden");
        caps.AspectRatio.Should().BeTrue();
        caps.AspectRatios.Should().Contain("3:4 (Portrait Standard)");
        caps.Resolutions.Should().NotBeNull().And.HaveElementAt(0, "1.0 MP");
        caps.ImagePrompt.Should().BeFalse();
    }

    [Fact]
    public void MetadataFor_ComfyUiId_YieldsWorkflowName()
    {
        var lines = _registry.MetadataFor("comfyui/My Workflow")!
            .Lines(new ImageGenerationParameters { Model = "comfyui/My Workflow", Prompt = "x" })
            .ToList();

        lines.Should().Contain("Workflow: My Workflow");
    }

    [Fact]
    public void IsSchemaKnown_EveryProductionDescriptorId_ReportsKnown()
    {
        // The predicate must never disagree with registration: every id this build ships a
        // descriptor for is known by definition. If a rename or a registration split ever makes
        // the two diverge, the UI starts labelling a supported model "not supported" — and this
        // three-line guard is what catches it.
        var production = ModelDescriptorRegistry.ProductionDescriptors();
        // Default() builds its dictionaries with OfType<IPayloadBuilder>(), so an entry that
        // doesn't implement it disappears in silence. Check that before trusting the loop below.
        production.Should().OnlyContain(d => d is IPayloadBuilder);

        foreach (var descriptor in production.OfType<IPayloadBuilder>())
            _registry.IsSchemaKnown(descriptor.ModelId).Should().BeTrue($"{descriptor.ModelId} ships a descriptor");
    }

    [Theory]
    [InlineData("stability-ai/generative")]
    [InlineData("bytedance/seedream-4")]
    // The new gpt-image model of 2026-11-18: allow-listed owner, no descriptor. The bug this
    // predicate exists to make visible.
    [InlineData("openai/gpt-image-3-turbo")]
    public void IsSchemaKnown_UnknownReplicatePath_ReportsUnknown(string modelId) =>
        _registry.IsSchemaKnown(modelId).Should().BeFalse();

    [Theory]
    // A seeded Pollinations id has a descriptor...
    [InlineData(ModelConstants.Pollinations.Flux)]
    // ...and an unseeded one rides the uniform Pollinations API. Both are known, because neither
    // is the app guessing at a field set.
    [InlineData("pollinations/some-model-shipped-tomorrow")]
    [InlineData("comfyui/Ideogram workflow_MR")]
    public void IsSchemaKnown_UniformProviders_ReportKnownRegardlessOfSeeding(string modelId) =>
        _registry.IsSchemaKnown(modelId).Should().BeTrue();

    [Theory]
    // Internal sentinels are lookup results, never model ids a catalog can contain.
    [InlineData("_fallback")]
    [InlineData("_fallback_comfyui")]
    // And an id PayloadFor refuses outright must not make the predicate throw: the UI calls it on
    // every model switch, including on ids that came from a stale cache.
    [InlineData("not-a-path")]
    public void IsSchemaKnown_ReportedUnknownWithoutThrowing_ForAnythingThatIsNotAModelWeKnow(string modelId)
    {
        _registry.IsSchemaKnown(modelId).Should().BeFalse();
    }

    /// <summary>
    /// The coverage check in <c>ModelCatalogCoordinator</c> reports a descriptor no catalog entry offers, so
    /// its input has to be exactly the set of real descriptors: the payload builders DI was given, and none
    /// of the three fallbacks. A fallback id in here would make that warning permanently true and therefore
    /// worthless, since fallbacks exist precisely to answer for models nobody described.
    /// </summary>
    [Fact]
    public void DescribedModelIds_AreThePayloadBuildersAndNeverTheFallbacks()
    {
        var builders = ModelDescriptorRegistry.ProductionDescriptors()
            .OfType<IPayloadBuilder>().Select(b => b.ModelId);

        _registry.DescribedModelIds.Should().BeEquivalentTo(builders);
        _registry.DescribedModelIds.Should().OnlyContain(id => !id.Contains("_fallback", StringComparison.Ordinal));
    }
}
