using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;
using Xunit;

namespace ImageGenerator.MAUI.Tests.Core.Domain.ComfyUi;

/// <summary>
/// Pins the contract of the sample workflow shipped in the repo (comfy-workflows/
/// Ideogram4-Sample.json, linked into the test output as a Content item): it must stay
/// patchable in BOTH prompt modes, keep its seed / resolution / %date% patch targets, and
/// never re-grow a custom-node-pack dependency — the README promises it runs on stock ComfyUI.
/// </summary>
public class ShippedSampleWorkflowTests
{
    private static readonly string SampleJson = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Ideogram4-Sample.json"));

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 9, 7, 5, 4, TimeSpan.Zero);

    private static ComfyUiRequest Request(
        string prompt = "p", bool json = false, long seed = 123,
        string? ar = null, double? mp = null) =>
        new("comfyui/Ideogram4-Sample", prompt, json, seed, ar, mp);

    [Fact]
    public void PlainMode_PatchesTheCaptionLiteralEncode()
    {
        var result = ComfyUiWorkflowPatcher.Patch(SampleJson, Request(prompt: "plain prompt"), FixedNow);

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["98:24"]!["inputs"]!["text"]!.GetValue<string>().Should().Be("plain prompt");
        result.PromptTargetDescription.Should().Contain("node 98:24");
    }

    [Fact]
    public void JsonMode_WithoutBuilderNode_TargetsTheCaptionJsonLiteral()
    {
        // The builder was stripped from the sample — structured JSON mode must keep working
        // via the caption-JSON CLIPTextEncode literal, which therefore has to PARSE as a
        // JSON object. Guards against a future re-export freezing a plain-text prompt.
        var result = ComfyUiWorkflowPatcher.Patch(
            SampleJson, Request(prompt: """{"high_level_description":"NEW"}""", json: true), FixedNow);

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["98:24"]!["inputs"]!["text"]!.GetValue<string>()
            .Should().Be("""{"high_level_description":"NEW"}""");
        result.PromptTargetDescription
            .Should().Contain("0 Ideogram4PromptBuilderKJ node(s)")
            .And.Contain("1 JSON-literal CLIPTextEncode node(s)");
    }

    [Fact]
    public void Seeds_TheRandomNoiseLiteralIsRerolled()
    {
        var result = ComfyUiWorkflowPatcher.Patch(SampleJson, Request(seed: 999), FixedNow);

        JsonNode.Parse(result.GraphJson)!["98:18"]!["inputs"]!["noise_seed"]!
            .GetValue<long>().Should().Be(999);
        result.SeedNodeIds.Should().BeEquivalentTo(["98:18"]);
    }

    [Fact]
    public void Resolution_PatchesTheSelectorNode()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            SampleJson, Request(ar: "9:16 (Portrait Widescreen)", mp: 1.0), FixedNow);

        var selector = JsonNode.Parse(result.GraphJson)!["37"]!["inputs"]!;
        selector["aspect_ratio"]!.GetValue<string>().Should().Be("9:16 (Portrait Widescreen)");
        selector["megapixels"]!.GetValue<double>().Should().Be(1.0);
        result.PromptTargetDescription.Should().Contain("1 ResolutionSelector");
    }

    [Fact]
    public void FilenamePrefix_DateTokensExpandToServerSafeText()
    {
        var result = ComfyUiWorkflowPatcher.Patch(SampleJson, Request(), FixedNow);

        var prefix = JsonNode.Parse(result.GraphJson)!["158"]!["inputs"]!["filename_prefix"]!
            .GetValue<string>();
        prefix.Should().Be("2026-06/Ideogram4_070504");
        result.PromptTargetDescription.Should().Contain("%date% expanded on 1 filename_prefix input(s)");
    }

    [Fact]
    public void SageAttention_CoversBothIdeogramModelLoadersAtRuntime()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            SampleJson, Request(), FixedNow, useSageAttention: true);
        var graph = JsonNode.Parse(result.GraphJson)!.AsObject();

        result.SageAttentionLoaderIds.Should().BeEquivalentTo(["98:23", "98:154"]);
        result.SageAttentionNodeIds.Should().HaveCount(2);
        result.SageAttentionNodeIds.Should().OnlyContain(id =>
            graph[id]!["class_type"]!.GetValue<string>() == "PathchSageAttentionKJ"
            && graph[id]!["inputs"]!["sage_attention"]!.GetValue<string>() == "auto");
    }

    [Fact]
    public void Portability_UsesNoCustomNodePackClasses()
    {
        // Verified against a live /object_info (2026-06-11): every remaining class is core
        // ComfyUI ("nodes" / "comfy_extras.*"). The two custom-pack classes the original
        // export carried must never come back.
        var classTypes = JsonNode.Parse(SampleJson)!.AsObject()
            .Select(kv => kv.Value!["class_type"]!.GetValue<string>())
            .ToList();

        classTypes.Should().NotContain("Ideogram4PromptBuilderKJ", "kjnodes is a custom pack");
        classTypes.Should().NotContain("Random Number", "WAS Node Suite is a custom pack");
    }

    [Fact]
    public void Sanitation_TheOnlyPromptLiteralsAreTheNeutralPlaceholder()
    {
        // Every string literal long enough to be a prompt must be the neutral placeholder
        // (or the quality-preset table) — a re-export from the live workflow would otherwise
        // leak the private prompt it carries.
        var graph = JsonNode.Parse(SampleJson)!.AsObject();
        var longLiterals = graph
            .SelectMany(kv => kv.Value!["inputs"]!.AsObject())
            .Where(input => input.Value?.GetValueKind() == JsonValueKind.String)
            .Select(input => input.Value!.GetValue<string>())
            .Where(text => text.Length > 200)
            .ToList();

        longLiterals.Should().NotBeEmpty();
        longLiterals.Should().OnlyContain(text =>
            text.Contains("ceramic coffee mug") || text.Contains("V4_QUALITY_48"));
    }
}
