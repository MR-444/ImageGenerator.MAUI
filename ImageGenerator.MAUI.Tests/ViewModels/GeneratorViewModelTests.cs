using FluentAssertions;
using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.ViewModels;
using Moq;
using ImageGenerator.MAUI.Services;

namespace ImageGenerator.MAUI.Tests.ViewModels
{
    public class GeneratorViewModelTests
    {
        private readonly GeneratorViewModel _viewModel;
        private readonly Mock<IImageGenerationService> _mockImageService;

        public GeneratorViewModelTests()
        {
            _mockImageService = new Mock<IImageGenerationService>();
            _viewModel = new GeneratorViewModel(_mockImageService.Object);
        }

        [Fact]
        public void AllModels_ShouldContainExpectedModels()
        {
            // Assert
            _viewModel.AllModels.Should().Contain(ModelConstants.OpenAI.GptImage1);
            _viewModel.AllModels.Should().Contain(ModelConstants.Flux.Dev);
            _viewModel.AllModels.Should().Contain(ModelConstants.Flux.Pro);
            _viewModel.AllModels.Should().Contain(ModelConstants.Flux.Pro11);
            _viewModel.AllModels.Should().Contain(ModelConstants.Flux.Schnell);
            _viewModel.AllModels.Should().Contain(ModelConstants.Flux.Pro11Ultra);
            _viewModel.AllModels.Should().Contain(ModelConstants.Flux.KontextMax);
            _viewModel.AllModels.Should().Contain(ModelConstants.Flux.KontextPro);
        }
    }
} 