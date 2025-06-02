using FluentAssertions;
using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.ViewModels;
using Moq;
using ImageGenerator.MAUI.Services;
using ImageGenerator.MAUI.Models;
using CommunityToolkit.Mvvm.Input;

namespace ImageGenerator.MAUI.Tests.ViewModels;

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

    [Fact]
    public async Task GenerateImage_WithValidParameters_ShouldGenerateImage()
    {
        // Arrange
        const string expectedImageData = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="; // 1x1 transparent PNG
        const string expectedMessage = "Success";
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";

        _mockImageService
            .Setup(x => x.GenerateImageAsync(It.IsAny<ImageGenerationParameters>()))
            .ReturnsAsync(new GeneratedImage { ImageDataBase64 = expectedImageData, Message = expectedMessage });

        // Act
        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand!).ExecuteAsync(null);

        // Assert
        _viewModel.StatusMessage.Should().Be(expectedMessage);
        _viewModel.StatusMessageColor.Should().Be(Colors.Green);
        _viewModel.GeneratedImagePath.Should().NotBeNull();
        _viewModel.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateImage_WithInvalidToken_ShouldShowError()
    {
        // Arrange
        _viewModel.Parameters.ApiToken = "";

        // Act
        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand!).ExecuteAsync(null);

        // Assert
        _viewModel.StatusMessage.Should().Be("API Token is required to generate images.");
        _viewModel.StatusMessageColor.Should().Be(Colors.Red);
        _viewModel.GeneratedImagePath.Should().BeNull();
        _viewModel.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public void UpdateCustomAspectRatio_WhenCustomSelected_ShouldEnableCustomInput()
    {
        // Arrange
        _viewModel.Parameters.AspectRatio = "custom";

        // Assert
        _viewModel.IsCustomAspectRatio.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithEmptyToken_ShouldSetInvalid()
    {
        // Arrange
        _viewModel.Parameters.ApiToken = "";

        // Assert
        _viewModel.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithValidToken_ShouldSetValid()
    {
        // Arrange
        _viewModel.Parameters.ApiToken = "valid-token";

        // Assert
        _viewModel.IsValid.Should().BeTrue();
    }

    [Fact]
    public void OnIsImageSelectedChanged_WhenTrue_ShouldAddMatchInputImageOption()
    {
        // Arrange
        _viewModel.IsImageSelected = true;

        // Assert
        _viewModel.AspectRatioOptions.Should().Contain("match_input_image");
        _viewModel.Parameters.AspectRatio.Should().Be("match_input_image");
    }

    [Fact]
    public void OnIsImageSelectedChanged_WhenFalse_ShouldRemoveMatchInputImageOption()
    {
        // Arrange
        _viewModel.IsImageSelected = true;
        _viewModel.IsImageSelected = false;

        // Assert
        _viewModel.AspectRatioOptions.Should().NotContain("match_input_image");
        _viewModel.Parameters.AspectRatio.Should().Be("16:9");
    }

    [Fact]
    public async Task GenerateImage_WhenServiceThrowsException_ShouldHandleError()
    {
        // Arrange
        _viewModel.Parameters.ApiToken = "valid-token";
        _mockImageService
            .Setup(x => x.GenerateImageAsync(It.IsAny<ImageGenerationParameters>()))
            .ThrowsAsync(new Exception("Test error"));

        // Act
        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand!).ExecuteAsync(null);

        // Assert
        _viewModel.StatusMessage.Should().Be("Error: Test error");
        _viewModel.StatusMessageColor.Should().Be(Colors.Red);
        _viewModel.GeneratedImagePath.Should().BeNull();
        _viewModel.IsGenerating.Should().BeFalse();
    }
}