using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Descriptors;

public class ModelDescriptorRegistryTests
{
    private readonly IModelDescriptorRegistry _registry = ModelDescriptorRegistry.Default();

    [Fact]
    public void PayloadFor_UnknownReplicatePath_ReturnsFallback()
    {
        var result = _registry.PayloadFor("stability-ai/some-new-model");

        // FallbackReplicateDescriptor declares the sentinel ModelId "_fallback".
        result.ModelId.Should().Be("_fallback");
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

        // Conservative defaults from FallbackReplicateDescriptor.
        caps.SafetyTolerance.Should().BeFalse();
        caps.PromptUpsampling.Should().BeFalse();
        caps.Seed.Should().BeTrue();
        caps.MaxImageInputs.Should().Be(1);
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

    [Fact]
    public void Seeds_CountMatchesProductionDescriptorSet()
    {
        // 6 originals (GPT 1.5, GPT 2, Flux Pro, Flux Pro Ultra, Klein4b, NanoBanana2)
        // + 3 pinned Ideogram V4 (balanced/turbo/quality)
        // + 3 Pollinations (flux/zimage/qwen-image) = 12.
        _registry.Seeds.Should().HaveCount(12);
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
}
