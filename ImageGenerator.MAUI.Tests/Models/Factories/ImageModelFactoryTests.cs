using FluentAssertions;
using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Models.Factories;
using ImageGenerator.MAUI.Models.Flux;
using ImageGenerator.MAUI.Models.Flux;
using ImageGenerator.MAUI.Models.OpenAi;

namespace ImageGenerator.MAUI.Tests.Models.Factories
{
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
                Prompt = "A robot creating art"
            };

            var expectedResult = new OpenAiRequest
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt
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
    }
}