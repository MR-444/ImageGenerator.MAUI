using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace ImageGenerator.MAUI.Tests.Core.Domain.ComfyUi;

/// <summary>
/// Pins the raw-variant contract of the Krea-2 sample (comfy-workflows/Krea2-Raw-Sample.json).
/// The graph shape matches Krea2-Sample.json except the negative branch — those patcher
/// behaviors are pinned by <see cref="Krea2SampleWorkflowTests"/> — so this class only pins
/// what makes raw RAW: the model card's own recipe (huggingface.co/krea/Krea-2-Raw: 52 steps,
/// cfg 3.5), user-verified on fireEngine 2026-07-17. Two hard-won pairings in that recipe:
/// real CFG needs a REAL empty-prompt negative (cfg&gt;1 against a ConditioningZeroOut negative
/// dissolves the render into grit), and eta must stay 0.0 (ancestral noise etches a canvas
/// pattern over the whole image).
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
    public void Negative_IsARealEmptyPromptEncode_NotZeroOut()
    {
        // The one deliberate shape difference vs the turbo sample: real CFG guides against
        // the empty-prompt unconditional, exactly like the model card's own pipeline. It must
        // also stay HIGHER-id than the positive encode (node 5) — the app's plain-prompt
        // patcher targets the lowest-id literal CLIPTextEncode.
        var negative = Graph["6"]!;
        negative["class_type"]!.GetValue<string>().Should().Be("CLIPTextEncode");
        negative["inputs"]!["text"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void Sampler_RunsTheModelCardRecipe()
    {
        var sampler = Graph["9"]!["inputs"]!;
        sampler["steps"]!.GetValue<int>().Should().Be(52, "the model card's recipe");
        sampler["cfg"]!.GetValue<double>().Should().Be(3.5, "the model card's recipe");
        sampler["eta"]!.GetValue<double>().Should().Be(0.0,
            "ancestral noise blurs the render under a canvas-like pattern");
        sampler["negative"]![0]!.GetValue<string>().Should().Be("6",
            "cfg 3.5 is only valid against the real empty-prompt negative");
    }

    [Fact]
    public void GraphShape_MatchesTheTurboSample_ExceptTheNegativeBranch()
    {
        var turbo = JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Krea2-Sample.json")))!.AsObject();

        static IEnumerable<(string Key, string Class)> Shape(JsonObject graph) =>
            graph.Where(kv => kv.Key != "6")
                .Select(kv => (kv.Key, kv.Value!["class_type"]!.GetValue<string>()));

        Shape(Graph).Should().BeEquivalentTo(Shape(turbo),
            "raw must stay a settings-only variant of the turbo sample apart from the negative");
        turbo["6"]!["class_type"]!.GetValue<string>().Should().Be("ConditioningZeroOut",
            "turbo keeps the zero-out negative its distilled guidance expects");
    }
}
