using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Factories;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Models.Factories;

public class ImageModelFactoryTests
{
    [Fact]
    public void CreateImageModel_WithFluxPro11_ReturnsCorrectModel()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Pro11,
            Prompt = "test prompt",
            Seed = 123,
            Width = 1024,
            Height = 1024,
            AspectRatio = "custom",
            ImagePrompt = "test image prompt",
            SafetyTolerance = 2,
            OutputFormat = ImageOutputFormat.Png,
            OutputQuality = 80,
            PromptUpsampling = true
        };

        var result = ImageModelFactory.CreateImageModel(parameters) as Flux11Pro;

        Assert.NotNull(result);
        Assert.Equal(ModelConstants.Flux.Pro11, result.ModelName);
        Assert.Equal("test prompt", result.Prompt);
        Assert.Equal(123, result.Seed);
        Assert.Equal(1024, result.Width);
        Assert.Equal(1024, result.Height);
        Assert.Equal("custom", result.AspectRatio);
        Assert.Equal("test image prompt", result.ImagePrompt);
        Assert.Equal(2, result.SafetyTolerance);
        Assert.Equal("png", result.OutputFormat);
        Assert.Equal(80, result.OutputQuality);
        Assert.True(result.PromptUpsampling);
    }

    [Fact]
    public void CreateImageModel_WithFluxPro11_WithPredefinedAspectRatio_ReturnsCorrectModel()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Pro11,
            Prompt = "test prompt",
            AspectRatio = "1:1",
            Width = 1024,
            Height = 1024
        };

        var result = ImageModelFactory.CreateImageModel(parameters) as Flux11Pro;

        Assert.NotNull(result);
        Assert.Null(result.Width);
        Assert.Null(result.Height);
        Assert.Equal("1:1", result.AspectRatio);
    }

    [Fact]
    public void CreateImageModel_WithFluxPro11Ultra_ReturnsCorrectModel()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Pro11Ultra,
            Prompt = "test prompt",
            Seed = 123,
            AspectRatio = "1:1",
            ImagePrompt = "test image prompt",
            SafetyTolerance = 2,
            OutputFormat = ImageOutputFormat.Jpg,
            Raw = true,
            ImagePromptStrength = 0.5f
        };

        var result = ImageModelFactory.CreateImageModel(parameters) as Flux11ProUltra;

        Assert.NotNull(result);
        Assert.Equal(ModelConstants.Flux.Pro11Ultra, result.ModelName);
        Assert.Equal(123, result.Seed);
        Assert.Equal("1:1", result.AspectRatio);
        Assert.Equal("test image prompt", result.ImagePrompt);
        Assert.True(result.Raw);
        Assert.Equal(0.5f, result.ImagePromptStrength);
    }

    [Fact]
    public void CreateImageModel_ShouldThrowArgumentException_ForUnknownModel()
    {
        var parameters = new ImageGenerationParameters { Model = "not-a-path", Prompt = "x" };

        var act = () => ImageModelFactory.CreateImageModel(parameters);

        act.Should().Throw<ArgumentException>().WithMessage("Unknown model type: not-a-path");
    }

    [Theory]
    [InlineData(ModelConstants.Flux.Klein4b)]
    [InlineData(ModelConstants.Flux.Flex2)]
    [InlineData(ModelConstants.Flux.Pro2)]
    [InlineData(ModelConstants.Flux.Max2)]
    public void CreateImageModel_Flux2Family_BuildsDictionaryWithExpectedKeys(string model)
    {
        var parameters = new ImageGenerationParameters
        {
            Model = model,
            Prompt = "a flux 2 test",
            Seed = 99,
            AspectRatio = "16:9",
            OutputFormat = ImageOutputFormat.Png,
            OutputQuality = 90
        };

        var result = ImageModelFactory.CreateImageModel(parameters);

        var dict = result.Should().BeOfType<Dictionary<string, object?>>().Subject;
        dict.Should().ContainKey("prompt").WhoseValue.Should().Be("a flux 2 test");
        dict.Should().ContainKey("aspect_ratio").WhoseValue.Should().Be("16:9");
        dict.Should().ContainKey("output_format").WhoseValue.Should().Be("png");
        dict.Should().ContainKey("output_quality").WhoseValue.Should().Be(90);
        dict.Should().ContainKey("seed").WhoseValue.Should().Be(99L);
        dict.Should().NotContainKey("safety_tolerance");
        dict.Should().NotContainKey("prompt_upsampling");
        // `images` key present with null value — wire-level null skipping is the converter's job.
        dict.Should().ContainKey("images");
    }

    [Fact]
    public void CreateImageModel_GptImage15OnReplicate_BuildsDictionaryWithAllKnobs()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.OpenAI.GptImage15OnReplicate,
            Prompt = "hello",
            AspectRatio = "1:1",
            OutputFormat = ImageOutputFormat.Jpg,
            OutputQuality = 75,
            GptQuality = "high",
            GptBackground = "transparent",
            GptModeration = "low",
            GptInputFidelity = "high"
        };

        var result = ImageModelFactory.CreateImageModel(parameters);

        var dict = result.Should().BeOfType<Dictionary<string, object?>>().Subject;
        dict.Should().ContainKey("prompt").WhoseValue.Should().Be("hello");
        dict.Should().ContainKey("aspect_ratio").WhoseValue.Should().Be("1:1");
        // Jpg -> jpeg translation for OpenAI-on-Replicate.
        dict.Should().ContainKey("output_format").WhoseValue.Should().Be("jpeg");
        dict.Should().ContainKey("output_compression").WhoseValue.Should().Be(75);
        dict.Should().ContainKey("quality").WhoseValue.Should().Be("high");
        dict.Should().ContainKey("background").WhoseValue.Should().Be("transparent");
        dict.Should().ContainKey("moderation").WhoseValue.Should().Be("low");
        dict.Should().ContainKey("input_fidelity").WhoseValue.Should().Be("high");
        dict.Should().NotContainKey("output_quality");
        dict.Should().NotContainKey("seed");
        dict.Should().ContainKey("input_images");
    }

    [Fact]
    public void CreateImageModel_NanoBanana2_BuildsDictionaryWithResolutionAndImageInput()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Google.NanoBanana2,
            Prompt = "a banana",
            AspectRatio = "16:9",
            Resolution = "2K",
            OutputFormat = ImageOutputFormat.Png
        };

        var result = ImageModelFactory.CreateImageModel(parameters);

        var dict = result.Should().BeOfType<Dictionary<string, object?>>().Subject;
        dict.Should().ContainKey("prompt").WhoseValue.Should().Be("a banana");
        dict.Should().ContainKey("aspect_ratio").WhoseValue.Should().Be("16:9");
        dict.Should().ContainKey("resolution").WhoseValue.Should().Be("2K");
        dict.Should().ContainKey("output_format").WhoseValue.Should().Be("png");
        // No seed / safety_tolerance / output_quality on nano-banana-2.
        dict.Should().NotContainKey("seed");
        dict.Should().NotContainKey("safety_tolerance");
        dict.Should().NotContainKey("output_quality");
        // image_input key present with null value — converter strips on the wire.
        dict.Should().ContainKey("image_input");
    }

    [Fact]
    public void CreateImageModel_NanoBanana2_WebpOutputFormat_CoercesToJpg()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Google.NanoBanana2,
            Prompt = "x",
            AspectRatio = "1:1",
            OutputFormat = ImageOutputFormat.Webp
        };

        var result = ImageModelFactory.CreateImageModel(parameters);

        var dict = (Dictionary<string, object?>)result;
        // nano-banana-2 schema rejects webp; factory coerces to jpg.
        dict["output_format"].Should().Be("jpg");
    }

    [Fact]
    public void CreateImageModel_UnknownReplicatePath_BuildsFallbackDictionary()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = "stability-ai/some-new-model",
            Prompt = "fallback test",
            Seed = 7,
            AspectRatio = "1:1",
            OutputFormat = ImageOutputFormat.Webp,
            OutputQuality = 80
        };

        var result = ImageModelFactory.CreateImageModel(parameters);

        var dict = result.Should().BeOfType<Dictionary<string, object?>>().Subject;
        dict.Keys.Should().BeEquivalentTo(["prompt", "seed", "aspect_ratio", "output_format", "output_quality"]);
    }
}
