using System.Text.Json.Nodes;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;
using Xunit;

namespace ImageGenerator.MAUI.Tests.Core.Domain.ComfyUi;

/// <summary>
/// Pins the contract of the upscale sample workflow (comfy-workflows/Upscale-Sample.json):
/// SDXL zavychroma + TTPlanet tile ControlNet + Ultimate SD Upscale, the stack verified on
/// the render host. Its stem contains "upscale" and it carries a LoadImage node — both are
/// what designates it as the "Upscale after render" chain target — and plain-prompt patching
/// must hit the POSITIVE tile-conditioning encode, never the baked negative.
/// </summary>
public class UpscaleSampleWorkflowTests
{
    private static readonly string SampleJson = File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Upscale-Sample.json"));

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 9, 7, 5, 4, TimeSpan.Zero);

    private static ComfyUiRequest Request(string prompt = "p", long seed = 123) =>
        new("comfyui/Upscale-Sample", prompt, UseJsonPrompt: false, seed, null, null);

    [Fact]
    public void IsDetectedAsALoadImageWorkflow()
    {
        ComfyUiWorkflowPatcher.HasLoadImageNode(SampleJson).Should().BeTrue(
            "LoadImage is what makes the Input Image card appear and the chain designation work");
    }

    [Fact]
    public void Patch_WritesTheUploadedNameSeedAndTilePrompt()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            SampleJson, Request(prompt: "the render's prompt", seed: 999), FixedNow,
            inputImageName: "emberforge_abc.png");

        var graph = JsonNode.Parse(result.GraphJson)!;
        graph["1"]!["inputs"]!["image"]!.GetValue<string>().Should().Be("emberforge_abc.png");
        graph["8"]!["inputs"]!["seed"]!.GetValue<long>().Should().Be(999);
        // Positive (node 3) gets the source prompt; the baked negative (node 4) must survive.
        graph["3"]!["inputs"]!["text"]!.GetValue<string>().Should().Be("the render's prompt");
        graph["4"]!["inputs"]!["text"]!.GetValue<string>().Should().Contain("blurry");
    }

    [Fact]
    public void FilenamePrefix_DateTokensExpandToServerSafeText()
    {
        var result = ComfyUiWorkflowPatcher.Patch(
            SampleJson, Request(), FixedNow, inputImageName: "x.png");

        JsonNode.Parse(result.GraphJson)!["9"]!["inputs"]!["filename_prefix"]!
            .GetValue<string>().Should().Be("2026-06/Upscale_070504");
    }

    [Fact]
    public void HostDependencies_NoNewNodePacksSneakIn()
    {
        // Everything a fireEngine upscale needs: stock nodes plus the UltimateSDUpscale pack.
        string[] allowed =
        [
            "LoadImage", "CheckpointLoaderSimple", "CLIPTextEncode", "ControlNetLoader",
            "ControlNetApplyAdvanced", "UpscaleModelLoader", "UltimateSDUpscale", "SaveImage",
        ];

        JsonNode.Parse(SampleJson)!.AsObject()
            .Select(kv => kv.Value!["class_type"]!.GetValue<string>())
            .Should().OnlyContain(classType => allowed.Contains(classType));
    }

    [Fact]
    public void TileAnchoring_TheControlNetAndSeamFixSettingsStayPinned()
    {
        // The anchor settings are the seam-defense: CN strength 0.6 ending at 85% plus
        // Half Tile seam fix at denoise 0.35. A casual re-export must not drift them.
        var graph = JsonNode.Parse(SampleJson)!;
        graph["6"]!["inputs"]!["strength"]!.GetValue<double>().Should().Be(0.6);
        graph["6"]!["inputs"]!["end_percent"]!.GetValue<double>().Should().Be(0.85);
        graph["8"]!["inputs"]!["denoise"]!.GetValue<double>().Should().Be(0.35);
        graph["8"]!["inputs"]!["seam_fix_mode"]!.GetValue<string>().Should().Be("Half Tile");
        graph["8"]!["inputs"]!["upscale_by"]!.GetValue<double>().Should().Be(2.0);
    }
}
