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
}
