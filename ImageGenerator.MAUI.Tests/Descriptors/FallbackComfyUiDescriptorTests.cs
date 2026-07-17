using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;

namespace ImageGenerator.MAUI.Tests.Descriptors;

public class FallbackComfyUiDescriptorTests
{
    private readonly FallbackComfyUiDescriptor _sut = new();

    private static ImageGenerationParameters Parameters(
        string model = "comfyui/Ideogram workflow_MR",
        string aspectRatio = "",
        string resolution = "") =>
        new()
        {
            Model = model,
            Prompt = "the prompt",
            UseJsonPrompt = true,
            Seed = 4242,
            AspectRatio = aspectRatio,
            Resolution = resolution
        };

    [Fact]
    public void Build_StripsThePrefix_IncludingNamesWithSpaces()
    {
        var request = (ComfyUiRequest)_sut.Build(Parameters());

        request.WorkflowName.Should().Be("Ideogram workflow_MR");
        request.Prompt.Should().Be("the prompt");
        request.UseJsonPrompt.Should().BeTrue();
        request.Seed.Should().Be(4242);
    }

    [Fact]
    public void Build_PassesAspectRatioOnlyWhenItIsAKnownSelectorOption()
    {
        var known = (ComfyUiRequest)_sut.Build(Parameters(aspectRatio: "16:9 (Widescreen)"));
        var unknown = (ComfyUiRequest)_sut.Build(Parameters(aspectRatio: "16:9"));

        known.AspectRatio.Should().Be("16:9 (Widescreen)");
        unknown.AspectRatio.Should().BeNull("unmapped values must leave the workflow's own AR");
    }

    [Theory]
    [InlineData("1.5 MP", 1.5)]
    [InlineData("1.0 MP", 1.0)]
    [InlineData("4.0 MP", 4.0)]
    public void Build_ParsesMegapixelsFromTheResolutionPreset(string resolution, double expected)
    {
        var request = (ComfyUiRequest)_sut.Build(Parameters(resolution: resolution));

        request.Megapixels.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Auto")]
    [InlineData("1440x2880")]
    public void Build_UnparseableResolution_YieldsNullMegapixels(string resolution)
    {
        var request = (ComfyUiRequest)_sut.Build(Parameters(resolution: resolution));

        request.Megapixels.Should().BeNull();
    }

    [Fact]
    public void Lines_IncludeTheBakedModelOnlyWhenKnown()
    {
        var unknown = Parameters();
        var known = Parameters();
        known.ComfyUiModelDisplay = "krea2_raw_bf16.safetensors";

        _sut.Lines(unknown).Should().NotContain(l => l.StartsWith("Model:"));
        _sut.Lines(known).Should().Contain("Model: krea2_raw_bf16.safetensors");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_EmptyComfyUiPreset_MapsToNullPresetChoice(string preset)
    {
        var parameters = Parameters();
        parameters.ComfyUiPreset = preset;

        var request = (ComfyUiRequest)_sut.Build(parameters);

        request.PresetChoice.Should().BeNull("empty means the workflow's own baked choice, no patch");
    }

    [Fact]
    public void Build_ComfyUiPreset_FlowsIntoTheRequest()
    {
        var parameters = Parameters();
        parameters.ComfyUiPreset = "Turbo";

        var request = (ComfyUiRequest)_sut.Build(parameters);

        request.PresetChoice.Should().Be("Turbo");
    }

    [Fact]
    public void Build_UpscaleFactorFlowsIntoTheRequest_AndLinesStayInvariant()
    {
        var defaulted = Parameters();
        var overridden = Parameters();
        overridden.ComfyUiUpscaleFactor = 2.5;

        ((ComfyUiRequest)_sut.Build(defaulted)).UpscaleFactor.Should().BeNull();
        ((ComfyUiRequest)_sut.Build(overridden)).UpscaleFactor.Should().Be(2.5);

        _sut.Lines(defaulted).Should().NotContain(l => l.StartsWith("UpscaleBy:"));
        // Invariant "2.5", never a culture-formatted "2,5" (de-DE machine).
        _sut.Lines(overridden).Should().Contain("UpscaleBy: 2.5");
    }

    [Fact]
    public void Build_FirstImagePromptFlowsIntoTheRequest()
    {
        var without = Parameters();
        var with = Parameters();
        with.ImagePrompts.Add("base64-image-1");
        with.ImagePrompts.Add("base64-image-2");

        ((ComfyUiRequest)_sut.Build(without)).InputImageBase64.Should().BeNull();
        ((ComfyUiRequest)_sut.Build(with)).InputImageBase64.Should().Be("base64-image-1",
            "ComfyUI LoadImage workflows take exactly one input image");
    }

    [Fact]
    public void Lines_IncludeThePresetOnlyWhenSet()
    {
        var defaulted = Parameters();
        var picked = Parameters();
        picked.ComfyUiPresetDisplay = "Turbo";

        _sut.Lines(defaulted).Should().NotContain(l => l.StartsWith("Preset:"));
        _sut.Lines(picked).Should().Contain("Preset: Turbo");
    }

    [Fact]
    public void Lines_BakedDefaultPreset_StillWritesThePresetLine()
    {
        // Regression: the user picks the workflow's baked-default preset (e.g. "Quality"), so
        // ComfyUiPreset is blanked (no patch) but ComfyUiPresetDisplay records the label. The
        // Preset line must read the display field — earlier it read the blanked sentinel and
        // dropped the line entirely for any baked-default selection.
        var bakedDefaultPick = Parameters();
        bakedDefaultPick.ComfyUiPreset = string.Empty;
        bakedDefaultPick.ComfyUiPresetDisplay = "Quality";

        _sut.Lines(bakedDefaultPick).Should().Contain("Preset: Quality");
    }

    // ---- Apply (Remix from an image) --------------------------------------------------------

    [Theory]
    [InlineData("1.5", "1.5 MP")]
    [InlineData("1.0", "1.0 MP")]
    [InlineData("4.0", "4.0 MP")]
    public void Apply_ReverseMapsMegapixelsToTheResolutionOption(string stored, string expectedOption)
    {
        var p = new ImageGenerationParameters();

        _sut.Apply(p, new Dictionary<string, string> { ["Megapixels"] = stored });

        p.Resolution.Should().Be(expectedOption);
    }

    [Fact]
    public void Apply_RoundTripsMegapixelsThroughLines()
    {
        var source = Parameters(resolution: "2.0 MP");

        var p = new ImageGenerationParameters();
        _sut.Apply(p, ParseLines(_sut.Lines(source)));

        p.Resolution.Should().Be("2.0 MP");
    }

    [Fact]
    public void Apply_UnknownMegapixels_LeavesResolutionUntouched()
    {
        var p = new ImageGenerationParameters { Resolution = "1.0 MP" };

        _sut.Apply(p, new Dictionary<string, string> { ["Megapixels"] = "7.3" });

        p.Resolution.Should().Be("1.0 MP");
    }

    [Fact]
    public void Apply_RestoresJsonPromptToggle()
    {
        var p = new ImageGenerationParameters { UseJsonPrompt = false };

        _sut.Apply(p, new Dictionary<string, string> { ["JsonPrompt"] = "True" });

        p.UseJsonPrompt.Should().BeTrue();
    }

    [Fact]
    public void Apply_LeavesModelAndPresetUntouched()
    {
        // Model/Preset lines are display-only provenance, so Remix deliberately does NOT
        // re-apply them — the workflow itself comes back via the model id.
        var p = new ImageGenerationParameters();

        _sut.Apply(p, new Dictionary<string, string>
        {
            ["Model"] = "server.safetensors",
            ["Preset"] = "Turbo"
        });

        p.ComfyUiModelDisplay.Should().BeEmpty();
        p.ComfyUiPreset.Should().BeEmpty();
    }

    private static IReadOnlyDictionary<string, string> ParseLines(IEnumerable<string> lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var value = line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            dict[line[..colon]] = value;
        }
        return dict;
    }
}
