using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace ImageGenerator.MAUI.Tests.Core.Domain.ComfyUi;

/// <summary>
/// Pins the raw-variant contract of the Krea-2 sample (comfy-workflows/Krea2-Raw-Sample.json).
/// The graph shape is identical to Krea2-Sample.json — those patcher behaviors are pinned by
/// <see cref="Krea2SampleWorkflowTests"/> — so this class only pins what makes raw RAW. The
/// values were A/B-verified on fireEngine (2026-07-17): eta 0.0 is the load-bearing one — 0.5
/// etches a canvas-like pattern over the whole image — and cfg must STAY 1.0, because any real
/// CFG against the zeroed-out negative dissolves the render into grit.
/// </summary>
public class Krea2RawSampleWorkflowTests
{
    private static readonly JsonObject Graph = JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Krea2-Raw-Sample.json")))!.AsObject();

    [Fact]
    public void Loader_IsTheRawBf16Model()
    {
        Graph["1"]!["inputs"]!["unet_name"]!.GetValue<string>()
            .Should().Be("krea2_raw_bf16.safetensors");
    }

    [Fact]
    public void TurboEnhancer_StaysInTheGraphButDisabled()
    {
        // Disabled is a verified clean pass-through; keeping the node makes the file diff
        // against the turbo sample minimal.
        Graph["4"]!["inputs"]!["enabled"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void Sampler_RunsTheVerifiedRawRecipe()
    {
        var sampler = Graph["9"]!["inputs"]!;
        sampler["steps"]!.GetValue<int>().Should().Be(28, "raw is not step-distilled");
        sampler["eta"]!.GetValue<double>().Should().Be(0.0,
            "ancestral noise (eta 0.5) blurs the render under a canvas-like pattern");
        sampler["cfg"]!.GetValue<double>().Should().Be(1.0,
            "real CFG against the zeroed-out negative dissolves the render into grit");
    }

    [Fact]
    public void GraphShape_MatchesTheTurboSampleNodeForNode()
    {
        var turbo = JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Krea2-Sample.json")))!.AsObject();

        Graph.Select(kv => (kv.Key, Class: kv.Value!["class_type"]!.GetValue<string>()))
            .Should().BeEquivalentTo(
                turbo.Select(kv => (kv.Key, Class: kv.Value!["class_type"]!.GetValue<string>())),
                "raw must stay a settings-only variant of the turbo sample");
    }
}
