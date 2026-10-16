using System.Text.Json.Nodes;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;

namespace ImageGenerator.MAUI.Tests.Core.Domain.ComfyUi;

/// <summary>
/// Contract and integrity tests for the physical Krea2-Sample.json file in TestAssets.
/// Ensures exported workflow templates adhere to host dependencies and clean baseline state.
/// </summary>
[Trait("Category", "Contract")]
public class Krea2AssetContractTests
{
    private static readonly string AssetPath =
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Krea2-Sample.json");

    private static string LoadSampleJson()
    {
        File.Exists(AssetPath).Should().BeTrue($"Required test asset missing at: {AssetPath}");
        return File.ReadAllText(AssetPath);
    }

    [Fact]
    public void HostDependencies_NoNewNodePacksSneakIn()
    {
        var json = LoadSampleJson();
        string[] allowed =
        [
            "UNETLoader", "CLIPLoader", "VAELoader", "CLIPTextEncode", "ConditioningZeroOut",
            "EmptyLatentImage", "VAEDecode", "SaveImage",
            "ResolutionSelector", "ClownsharKSampler_Beta", "ComfyUI-Krea2T-Enhancer",
        ];

        var classTypes = JsonNode.Parse(json)!.AsObject()
            .Select(kv => kv.Value!["class_type"]!.GetValue<string>());

        classTypes.Should().BeSubsetOf(allowed);
    }

    [Fact]
    public void Sanitation_ThePromptLiteralIsTheNeutralPlaceholder()
    {
        var json = LoadSampleJson();
        var graph = JsonNode.Parse(json)!.AsObject();

        graph["5"]!["inputs"]!["text"]!.GetValue<string>()
            .Should().Contain("ceramic coffee mug");
    }

    [Fact]
    public void ShippedAsset_CanBeFullyPatchedWithoutErrors()
    {
        var json = LoadSampleJson();
        var request = new ComfyUiRequest(
            WorkflowName: "Krea2-Sample",
            Prompt: "Test prompt",
            UseJsonPrompt: false,
            Seed: 42,
            AspectRatio: "1:1 (Square)",
            Megapixels: 1.0);

        var act = () => ComfyUiWorkflowPatcher.Patch(json, request, DateTimeOffset.UtcNow);

        act.Should().NotThrow();
    }
}