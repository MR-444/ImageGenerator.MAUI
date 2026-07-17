using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;
using Xunit;

namespace ImageGenerator.MAUI.Tests.Core.Domain.ComfyUi;

/// <summary>
/// Pins the contract of the Krea-2 sample workflow (comfy-workflows/Krea2-Sample.json).
/// Unlike the Ideogram sample it is plain-prompt ONLY (Krea-2 takes natural language; the
/// Qwen3-VL encoder renders a V4 JSON caption as literal text garbage), and it deliberately
/// depends on custom node packs present on the render host: RES4LYF (ClownsharKSampler_Beta),
/// the Krea2T-Enhancer model patch, and the ResolutionSelector pack.
/// </summary>
public class Krea2SampleWorkflowTests
{
    private static readonly string SampleJson = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Krea2-Sample.json"));

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 9, 7, 5, 4, TimeSpan.Zero);

    private static ComfyUiRequest Request(
        string prompt = "p", bool json = false, long seed = 123,
        string? ar = null, double? mp = null) =>
        new("comfyui/Krea2-Sample", prompt, json, seed, ar, mp);

    [Fact]
    public void PlainMode_PatchesTheSinglePromptEncode()
    {
        var result = ComfyUiWorkflowPatcher.Patch(SampleJson, Request(prompt: "plain prompt"), FixedNow);

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["5"]!["inputs"]!["text"]!.GetValue<string>().Should().Be("plain prompt");
        result.PromptTargetDescription.Should().Contain("node 5");
    }

    [Fact]
    public void JsonMode_ThrowsWithTheActionableMessage()
    {
        // Krea-2 must stay plain-prompt only: no builder node and no caption-JSON literal.
        // A silent fallthrough here would feed raw V4 JSON to the Qwen3-VL encoder and
        // reproduce the garbled-output failure this guard exists to prevent.
        var act = () => ComfyUiWorkflowPatcher.Patch(
            SampleJson, Request(prompt: """{"high_level_description":"x"}""", json: true), FixedNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*uncheck 'Structured JSON prompt'*");
    }

    [Fact]
    public void Seeds_TheSamplerSeedLiteralIsRerolled()
    {
        var result = ComfyUiWorkflowPatcher.Patch(SampleJson, Request(seed: 999), FixedNow);

        JsonNode.Parse(result.GraphJson)!["9"]!["inputs"]!["seed"]!
            .GetValue<long>().Should().Be(999);
        result.SeedNodeIds.Should().BeEquivalentTo(["9"]);
    }

    [Fact]
    public void Resolution_PatchesTheSelectorNode()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            SampleJson, Request(ar: "9:16 (Portrait Widescreen)", mp: 1.0), FixedNow);

        var selector = JsonNode.Parse(result.GraphJson)!["7"]!["inputs"]!;
        selector["aspect_ratio"]!.GetValue<string>().Should().Be("9:16 (Portrait Widescreen)");
        selector["megapixels"]!.GetValue<double>().Should().Be(1.0);
        result.PromptTargetDescription.Should().Contain("1 ResolutionSelector");
    }

    [Fact]
    public void FilenamePrefix_DateTokensExpandToServerSafeText()
    {
        var result = ComfyUiWorkflowPatcher.Patch(SampleJson, Request(), FixedNow);

        JsonNode.Parse(result.GraphJson)!["11"]!["inputs"]!["filename_prefix"]!
            .GetValue<string>().Should().Be("2026-06/Krea2_070504");
    }

    [Fact]
    public void SageAttention_PatchesTheLoaderBeforeTheEnhancer()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            SampleJson, Request(), FixedNow, useSageAttention: true);
        var graph = JsonNode.Parse(result.GraphJson)!.AsObject();

        result.SageAttentionLoaderIds.Should().BeEquivalentTo(["1"]);
        var sageId = result.SageAttentionNodeIds.Should().ContainSingle().Subject;
        graph[sageId]!["class_type"]!.GetValue<string>().Should().Be("PathchSageAttentionKJ");
        graph[sageId]!["inputs"]!["sage_attention"]!.GetValue<string>().Should().Be("auto");
        // The whole sampling stack must run saged: the enhancer consumes the patch node.
        graph["4"]!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be(sageId);
    }

    [Fact]
    public void HostDependencies_NoNewNodePacksSneakIn()
    {
        // These classes are all a fireEngine render needs; a re-export must not grow the set.
        string[] allowed =
        [
            "UNETLoader", "CLIPLoader", "VAELoader", "CLIPTextEncode", "ConditioningZeroOut",
            "EmptyLatentImage", "VAEDecode", "SaveImage",
            "ResolutionSelector", "ClownsharKSampler_Beta", "ComfyUI-Krea2T-Enhancer",
        ];

        JsonNode.Parse(SampleJson)!.AsObject()
            .Select(kv => kv.Value!["class_type"]!.GetValue<string>())
            .Should().OnlyContain(classType => allowed.Contains(classType));
    }

    [Fact]
    public void Sanitation_ThePromptLiteralIsTheNeutralPlaceholder()
    {
        var graph = JsonNode.Parse(SampleJson)!.AsObject();

        graph["5"]!["inputs"]!["text"]!.GetValue<string>()
            .Should().Contain("ceramic coffee mug");
    }
}
