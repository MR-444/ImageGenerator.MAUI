using System.Text.Json.Nodes;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;

namespace ImageGenerator.MAUI.Tests.Core.Domain.ComfyUi;

/// <summary>
/// Unit tests for <see cref="ComfyUiWorkflowPatcher"/> behavior against Krea-2 graphs.
/// Runs entirely in memory with no filesystem dependencies.
/// </summary>
public class Krea2WorkflowPatcherTests
{
    // Minimal standard Krea-2 workflow graph in-memory fixture
    private const string Krea2WorkflowJson = """
    {
      "1": {
        "class_type": "UNETLoader",
        "inputs": { "unet_name": "Krea2-Turbo.safetensors" }
      },
      "2": {
        "class_type": "CLIPLoader",
        "inputs": { "clip_name": "qwen3vl.safetensors", "type": "krea2" }
      },
      "4": {
        "class_type": "ComfyUI-Krea2T-Enhancer",
        "inputs": { "model": ["1", 0], "enabled": true }
      },
      "5": {
        "class_type": "CLIPTextEncode",
        "inputs": { "clip": ["2", 0], "text": "placeholder prompt" }
      },
      "7": {
        "class_type": "ResolutionSelector",
        "inputs": { "aspect_ratio": "1:1 (Square)", "megapixels": 1.0 }
      },
      "9": {
        "class_type": "ClownsharKSampler_Beta",
        "inputs": { "seed": 111, "model": ["4", 0], "positive": ["5", 0] }
      },
      "11": {
        "class_type": "SaveImage",
        "inputs": { "filename_prefix": "%date:yyyy-MM%/Krea2_%date:hhmmss%" }
      }
    }
    """;

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 9, 7, 5, 4, TimeSpan.Zero);

    private static ComfyUiRequest CreateRequest(
        string prompt = "p", bool json = false, long seed = 123,
        string? ar = null, double? mp = null, string? lora = null,
        double loraStrength = 1.0) =>
        new("comfyui/Krea2-Sample", prompt, json, seed, ar, mp,
            LoraName: lora, LoraStrength: loraStrength);

    [Fact]
    public void WorkflowProbe_RecognizesTheLiteralKrea2ClipType()
    {
        ComfyUiWorkflowPatcher.IsKrea2Workflow(Krea2WorkflowJson).Should().BeTrue();
        ComfyUiWorkflowPatcher.IsKrea2Workflow(
            """{ "2": { "class_type": "CLIPLoader", "inputs": { "type": "flux" } } }""")
            .Should().BeFalse();
        ComfyUiWorkflowPatcher.IsKrea2Workflow("not json").Should().BeFalse();
    }

    [Fact]
    public void PlainMode_PatchesTheSinglePromptEncode()
    {
        var result = ComfyUiWorkflowPatcher.Patch(Krea2WorkflowJson, CreateRequest(prompt: "plain prompt"), FixedNow);

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["5"]!["inputs"]!["text"]!.GetValue<string>().Should().Be("plain prompt");
        result.PromptTargetDescription.Should().Contain("node 5");
    }

    [Fact]
    public void JsonMode_ThrowsWithTheActionableMessage()
    {
        var act = () => ComfyUiWorkflowPatcher.Patch(
            Krea2WorkflowJson, CreateRequest(prompt: """{"high_level_description":"x"}""", json: true), FixedNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*uncheck 'Structured JSON prompt'*");
    }

    [Fact]
    public void FilenamePrefix_DateTokensExpandToServerSafeText()
    {
        var result = ComfyUiWorkflowPatcher.Patch(Krea2WorkflowJson, CreateRequest(), FixedNow);

        JsonNode.Parse(result.GraphJson)!["11"]!["inputs"]!["filename_prefix"]!
            .GetValue<string>().Should().Be("2026-06/Krea2_070504");
    }

    [Fact]
    public void SageAttention_PatchesTheLoaderBeforeTheEnhancer()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            Krea2WorkflowJson, CreateRequest(), FixedNow, useSageAttention: true);
        var graph = JsonNode.Parse(result.GraphJson)!.AsObject();

        result.SageAttentionLoaderIds.Should().BeEquivalentTo(["1"]);
        var sageId = result.SageAttentionNodeIds.Should().ContainSingle().Subject;
        graph[sageId]!["class_type"]!.GetValue<string>().Should().Be("PathchSageAttentionKJ");
        graph[sageId]!["inputs"]!["sage_attention"]!.GetValue<string>().Should().Be("auto");
        graph["4"]!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be(sageId);
    }

    [Fact]
    public void Lora_InjectsBuiltInModelOnlyLoaderBeforeTheEnhancer()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            Krea2WorkflowJson, CreateRequest(lora: @"Krea2\portrait.safetensors", loraStrength: 0.75), FixedNow);
        var graph = JsonNode.Parse(result.GraphJson)!.AsObject();

        graph.ContainsKey("emberforge:krea2-lora").Should().BeTrue();
        var lora = graph["emberforge:krea2-lora"]!;
        lora["class_type"]!.GetValue<string>().Should().Be("LoraLoaderModelOnly");
        lora["inputs"]!["model"]![0]!.GetValue<string>().Should().Be("1");
        lora["inputs"]!["lora_name"]!.GetValue<string>().Should().Be(@"Krea2\portrait.safetensors");
        lora["inputs"]!["strength_model"]!.GetValue<double>().Should().Be(0.75);
        graph["4"]!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be("emberforge:krea2-lora");
    }

    [Fact]
    public void Lora_WithSageAttention_PreservesLoaderSageLoraEnhancerOrder()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            Krea2WorkflowJson, CreateRequest(lora: "style.safetensors"), FixedNow, useSageAttention: true);
        var graph = JsonNode.Parse(result.GraphJson)!.AsObject();
        var sageId = result.SageAttentionNodeIds.Should().ContainSingle().Subject;

        graph[sageId]!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be("1");
        graph["emberforge:krea2-lora"]!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be(sageId);
        graph["4"]!["inputs"]!["model"]![0]!.GetValue<string>().Should().Be("emberforge:krea2-lora");
    }
}
