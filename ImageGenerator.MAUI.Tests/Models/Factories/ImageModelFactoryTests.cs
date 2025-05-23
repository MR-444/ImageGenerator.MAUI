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

            // Act
            var result = ImageModelFactory.CreateImageModel(parameters);

            // Assert
            result.Should().BeOfType<Flux11Pro>();
            var flux11Pro = result as Flux11Pro;
            flux11Pro.Should().NotBeNull();
            flux11Pro.ModelName.Should().Be(parameters.Model);
            flux11Pro.Prompt.Should().Be(parameters.Prompt);
            flux11Pro.PromptUpsampling.Should().Be(parameters.PromptUpsampling);
            flux11Pro.Seed.Should().Be(parameters.Seed);
            flux11Pro.Width.Should().Be(parameters.Width);
            flux11Pro.Height.Should().Be(parameters.Height);
            flux11Pro.AspectRatio.Should().Be(parameters.AspectRatio);
            flux11Pro.ImagePrompt.Should().Be(parameters.ImagePrompt);
            flux11Pro.SafetyTolerance.Should().Be(parameters.SafetyTolerance);
            flux11Pro.OutputFormat.Should().Be(parameters.OutputFormat);
            flux11Pro.OutputQuality.Should().Be(parameters.OutputQuality);
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

            // Act
            var result = ImageModelFactory.CreateImageModel(parameters);

            // Assert
            result.Should().BeOfType<Flux11ProUltra>();
            var flux11ProUltra = result as Flux11ProUltra;
            flux11ProUltra.Should().NotBeNull();
            flux11ProUltra.ModelName.Should().Be(parameters.Model);
            flux11ProUltra.Prompt.Should().Be(parameters.Prompt);
            flux11ProUltra.Seed.Should().Be(parameters.Seed);
            flux11ProUltra.AspectRatio.Should().Be(parameters.AspectRatio);
            flux11ProUltra.ImagePrompt.Should().Be(parameters.ImagePrompt);
            flux11ProUltra.SafetyTolerance.Should().Be(parameters.SafetyTolerance);
            flux11ProUltra.OutputFormat.Should().Be(parameters.OutputFormat);
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

            // Act
            var result = ImageModelFactory.CreateImageModel(parameters);

            // Assert
            result.Should().BeOfType<OpenAiRequest>();
            var openAiRequest = result as OpenAiRequest;
            openAiRequest.Should().NotBeNull();
            openAiRequest.ModelName.Should().Be(parameters.Model);
            openAiRequest.Prompt.Should().Be(parameters.Prompt);
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
            Action act = () => ImageModelFactory.CreateImageModel(parameters);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Unknown model type: unknown-model");
        }
    }
}