using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Factories;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Models.Factories;

public class ImageModelFactoryTests
{
    [Fact]
    public void CreateImageModel_WithFluxPro11_ReturnsCorrectModel()
    {
        // Arrange
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

        // Act
        var result = ImageModelFactory.CreateImageModel(parameters) as Flux11Pro;

        // Assert
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
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Pro11,
            Prompt = "test prompt",
            Seed = 123,
            Width = 1024,
            Height = 1024,
            AspectRatio = "1:1",
            ImagePrompt = "test image prompt",
            SafetyTolerance = 2,
            OutputFormat = ImageOutputFormat.Png,
            OutputQuality = 80,
            PromptUpsampling = true
        };

        // Act
        var result = ImageModelFactory.CreateImageModel(parameters) as Flux11Pro;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ModelConstants.Flux.Pro11, result.ModelName);
        Assert.Equal("test prompt", result.Prompt);
        Assert.Equal(123, result.Seed);
        Assert.Null(result.Width);
        Assert.Null(result.Height);
        Assert.Equal("1:1", result.AspectRatio);
        Assert.Equal("test image prompt", result.ImagePrompt);
        Assert.Equal(2, result.SafetyTolerance);
        Assert.Equal("png", result.OutputFormat);
        Assert.Equal(80, result.OutputQuality);
        Assert.True(result.PromptUpsampling);
    }

    [Fact]
    public void CreateImageModel_WithFluxPro11Ultra_ReturnsCorrectModel()
    {
        // Arrange
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

        // Act
        var result = ImageModelFactory.CreateImageModel(parameters) as Flux11ProUltra;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ModelConstants.Flux.Pro11Ultra, result.ModelName);
        Assert.Equal("test prompt", result.Prompt);
        Assert.Equal(123, result.Seed);
        Assert.Equal("1:1", result.AspectRatio);
        Assert.Equal("test image prompt", result.ImagePrompt);
        Assert.Equal(2, result.SafetyTolerance);
        Assert.Equal("jpg", result.OutputFormat);
        Assert.True(result.Raw);
        Assert.Equal(0.5f, result.ImagePromptStrength);
    }

    [Fact]
    public void CreateImageModel_WithFluxDev_ReturnsCorrectModel()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Dev,
            Prompt = "test prompt",
            Seed = 123,
            AspectRatio = "1:1",
            ImagePrompt = "test image prompt",
            SafetyTolerance = 2,
            OutputFormat = ImageOutputFormat.Webp,
            OutputQuality = 80
        };

        // Act
        var result = ImageModelFactory.CreateImageModel(parameters) as FluxDev;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ModelConstants.Flux.Dev, result.ModelName);
        Assert.Equal("test prompt", result.Prompt);
        Assert.Equal(123, result.Seed);
        Assert.Equal("1:1", result.AspectRatio);
        Assert.Equal("test image prompt", result.ImagePrompt);
        Assert.Equal(2, result.SafetyTolerance);
        Assert.Equal("webp", result.OutputFormat);
        Assert.Equal(80, result.OutputQuality);
    }

    [Fact]
    public void CreateImageModel_WithFluxSchnell_ReturnsCorrectModel()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Schnell,
            Prompt = "test prompt",
            Seed = 123,
            AspectRatio = "1:1",
            ImagePrompt = "test image prompt",
            SafetyTolerance = 2,
            OutputFormat = ImageOutputFormat.Png,
            OutputQuality = 80
        };

        // Act
        var result = ImageModelFactory.CreateImageModel(parameters) as FluxSchnell;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ModelConstants.Flux.Schnell, result.ModelName);
        Assert.Equal("test prompt", result.Prompt);
        Assert.Equal(123, result.Seed);
        Assert.Equal("1:1", result.AspectRatio);
        Assert.Equal("test image prompt", result.ImagePrompt);
        Assert.Equal(2, result.SafetyTolerance);
        Assert.Equal("png", result.OutputFormat);
        Assert.Equal(80, result.OutputQuality);
    }

    [Fact]
    public void CreateImageModel_ShouldReturnOpenAiRequest_ForGptImage1ModelName()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.OpenAI.GptImage1,
            Prompt = "A robot creating art",
            AspectRatio = "1024x1024"
        };

        var expectedResult = new OpenAiRequest
        {
            ModelName = parameters.Model,
            Prompt = parameters.Prompt,
            Size = "1024x1024"
        };

        // Act
        var result = ImageModelFactory.CreateImageModel(parameters);

        // Assert
        result.Should().BeOfType<OpenAiRequest>();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public void CreateImageModel_ShouldThrowArgumentException_ForUnknownModel()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = "unknown-model",
            Prompt = "An unknown model"
        };

        // Act
        var act = () => ImageModelFactory.CreateImageModel(parameters);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Unknown model type: unknown-model");
    }

    [Fact]
    public void CreateImageModel_ShouldReturnFluxKontextMax_ForFluxKontextMaxModelName()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextMax,
            Prompt = "A contextual image",
            Seed = 54321,
            AspectRatio = "match_input_image",
            ImagePrompt = "base64ImageData"
        };

        var expectedResult = new FluxKontextMax
        {
            ModelName = parameters.Model,
            Prompt = parameters.Prompt,
            Seed = parameters.Seed,
            AspectRatio = parameters.AspectRatio,
            InputImage = parameters.ImagePrompt
        };

        // Act
        var result = ImageModelFactory.CreateImageModel(parameters);

        // Assert
        result.Should().BeOfType<FluxKontextMax>();
        result.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public void CreateImageModel_ShouldReturnFluxKontextPro_ForFluxKontextProModelName()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A professional contextual image",
            Seed = 98765,
            AspectRatio = "match_input_image",
            ImagePrompt = "base64ImageData"
        };

        var expectedResult = new FluxKontextPro
        {
            ModelName = parameters.Model,
            Prompt = parameters.Prompt,
            Seed = parameters.Seed,
            AspectRatio = parameters.AspectRatio,
            InputImage = parameters.ImagePrompt
        };

        // Act
        var result = ImageModelFactory.CreateImageModel(parameters);

        // Assert
        result.Should().BeOfType<FluxKontextPro>();
        result.Should().BeEquivalentTo(expectedResult);
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
        dict.Should().ContainKey("images");
    }

    [Fact]
    public void CreateImageModel_GptImage15OnReplicate_BuildsDictionaryWithOutputCompression()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.OpenAI.GptImage15OnReplicate,
            Prompt = "hello",
            AspectRatio = "1:1",
            OutputFormat = ImageOutputFormat.Jpg,
            OutputQuality = 75
        };

        var result = ImageModelFactory.CreateImageModel(parameters);

        var dict = result.Should().BeOfType<Dictionary<string, object?>>().Subject;
        dict.Should().ContainKey("prompt").WhoseValue.Should().Be("hello");
        dict.Should().ContainKey("aspect_ratio").WhoseValue.Should().Be("1:1");
        // Jpg -> jpeg translation for OpenAI-on-Replicate.
        dict.Should().ContainKey("output_format").WhoseValue.Should().Be("jpeg");
        dict.Should().ContainKey("output_compression").WhoseValue.Should().Be(75);
        dict.Should().NotContainKey("output_quality");
        dict.Should().NotContainKey("seed");
        dict.Should().ContainKey("input_images");
    }

    [Fact]
    public void CreateImageModel_UnknownReplicatePath_BuildsFallbackDictionary()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = "google/nano-banana-2",
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

    [Fact]
    public void CreateImageModel_MalformedModel_StillThrows()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = "not-a-path",
            Prompt = "x"
        };

        var act = () => ImageModelFactory.CreateImageModel(parameters);
        act.Should().Throw<ArgumentException>();
    }
}