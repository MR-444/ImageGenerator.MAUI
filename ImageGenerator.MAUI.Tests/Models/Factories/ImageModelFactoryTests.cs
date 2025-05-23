using FluentAssertions;
using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Models.Factories;
using ImageGenerator.MAUI.Models.Flux;
using ImageGenerator.MAUI.Models.OpenAi;

namespace ImageGenerator.MAUI.Tests.Models.Factories
{
    public class ImageModelFactoryTests
    {
        [Fact]
        public void CreateImageModel_ShouldReturnFlux11Pro_ForFlux11ProModelName()
        {
            // Arrange
            var parameters = new ImageGenerationParameters
            {
                Model = "black-forest-labs/flux-1.1-pro",
                Prompt = "A beautiful landscape",
                PromptUpsampling = true,
                Seed = 12345,
                Width = 1920,
                Height = 1080,
                AspectRatio = "16:9",
                ImagePrompt = "Forest",
                SafetyTolerance = 6,
                OutputFormat = ImageOutputFormat.Png,
                OutputQuality = 90
            };

            var expectedResult = new Flux11Pro
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                PromptUpsampling = parameters.PromptUpsampling,
                Seed = parameters.Seed,
                Width = parameters.Width,
                Height = parameters.Height,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat,
                OutputQuality = parameters.OutputQuality
            };

            // Act
            var result = ImageModelFactory.CreateImageModel(parameters);

            // Assert
            result.Should().BeOfType<Flux11Pro>();
            result.Should().BeEquivalentTo(expectedResult);
        }

        [Fact]
        public void CreateImageModel_ShouldReturnFlux11ProUltra_ForFlux11ProUltraModelName()
        {
            // Arrange
            var parameters = new ImageGenerationParameters
            {
                Model = "flux-1.1-pro-ultra",
                Prompt = "A futuristic cityscape",
                Seed = 98765,
                AspectRatio = "21:9",
                ImagePrompt = "Sci-Fi",
                SafetyTolerance = 6,
                OutputFormat = ImageOutputFormat.Png
            };

            var expectedResult = new Flux11ProUltra
            {
                ModelName = parameters.Model,
                Prompt = parameters.Prompt,
                Seed = parameters.Seed,
                AspectRatio = parameters.AspectRatio,
                ImagePrompt = parameters.ImagePrompt,
                SafetyTolerance = parameters.SafetyTolerance,
                OutputFormat = parameters.OutputFormat
            };

            // Act
            var result = ImageModelFactory.CreateImageModel(parameters);

            // Assert
            result.Should().BeOfType<Flux11ProUltra>();
            result.Should().BeEquivalentTo(expectedResult);
        }

        [Fact]
        public void CreateImageModel_ShouldReturnOpenAiRequest_ForGptImage1ModelName()
        {
            // Arrange
            var parameters = new ImageGenerationParameters
            {
                Model = "gpt-image-1",
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
    }
}