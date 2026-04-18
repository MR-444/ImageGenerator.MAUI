using FluentAssertions;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Moq;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Shared.Constants;

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
        var values = _viewModel.AllModels.Select(m => m.Value).ToList();
        values.Should().Contain(ModelConstants.OpenAI.GptImage1);
        values.Should().Contain(ModelConstants.Flux.Dev);
        values.Should().Contain(ModelConstants.Flux.Pro);
        values.Should().Contain(ModelConstants.Flux.Pro11);
        values.Should().Contain(ModelConstants.Flux.Schnell);
        values.Should().Contain(ModelConstants.Flux.Pro11Ultra);
        values.Should().Contain(ModelConstants.Flux.KontextMax);
        values.Should().Contain(ModelConstants.Flux.KontextPro);
    }

    [Fact]
    public void Providers_ShouldIncludeAllAndDistinctProviders()
    {
        _viewModel.Providers.Should().Contain("All providers");
        _viewModel.Providers.Should().Contain("OpenAI");
        _viewModel.Providers.Should().Contain("Black Forest Labs");
    }

    [Fact]
    public void SelectedProvider_WhenSet_FiltersModels()
    {
        _viewModel.SelectedProvider = "OpenAI";

        _viewModel.FilteredModels.Should().OnlyContain(m => m.Provider == "OpenAI");
        _viewModel.FilteredModels.Should().HaveCount(1);
    }

    [Fact]
    public void SelectedModel_WhenChanged_UpdatesParametersModel()
    {
        var target = _viewModel.AllModels.First(m => m.Value == ModelConstants.Flux.Schnell);

        _viewModel.SelectedModel = target;

        _viewModel.Parameters.Model.Should().Be(ModelConstants.Flux.Schnell);
    }

    [Fact]
    public async Task GenerateImage_WithValidParameters_ShouldGenerateImage()
    {
        const string expectedImageData = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";

        _mockImageService
            .Setup(x => x.GenerateImageAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedImage { ImageDataBase64 = expectedImageData, Message = "ok" });

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.StatusMessage.Should().StartWith("Saved to ");
        _viewModel.StatusKind.Should().Be(StatusKind.Success);
        _viewModel.GeneratedImagePath.Should().NotBeNull();
        _viewModel.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateImage_WithMissingFields_ShouldShowError()
    {
        _viewModel.Parameters.ApiToken = "";
        _viewModel.Parameters.Prompt = "";

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.StatusMessage.Should().Contain("API Token");
        _viewModel.StatusMessage.Should().Contain("Prompt");
        _viewModel.StatusKind.Should().Be(StatusKind.Error);
        _viewModel.GeneratedImagePath.Should().BeNull();
        _viewModel.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public void UpdateCustomAspectRatio_WhenCustomSelected_ShouldEnableCustomInput()
    {
        _viewModel.Parameters.AspectRatio = "custom";

        _viewModel.IsCustomAspectRatio.Should().BeTrue();
    }

    [Fact]
    public void ValidateParameters_WithEmptyToken_ShouldSetInvalid()
    {
        _viewModel.Parameters.ApiToken = "";
        _viewModel.Parameters.Prompt = "anything";

        _viewModel.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithEmptyPrompt_ShouldSetInvalid()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "";

        _viewModel.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateParameters_WithTokenAndPrompt_ShouldSetValid()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "a cat on a sofa";

        _viewModel.IsValid.Should().BeTrue();
    }

    [Fact]
    public void OnIsImageSelectedChanged_WhenTrue_ShouldAddMatchInputImageOption()
    {
        _viewModel.IsImageSelected = true;

        _viewModel.AspectRatioOptions.Should().Contain("match_input_image");
        _viewModel.Parameters.AspectRatio.Should().Be("match_input_image");
    }

    [Fact]
    public void OnIsImageSelectedChanged_WhenFalse_ShouldRemoveMatchInputImageOption()
    {
        _viewModel.IsImageSelected = true;
        _viewModel.IsImageSelected = false;

        _viewModel.AspectRatioOptions.Should().NotContain("match_input_image");
        _viewModel.Parameters.AspectRatio.Should().Be("16:9");
    }

    [Fact]
    public async Task GenerateImage_WhenServiceThrowsException_ShouldHandleError()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockImageService
            .Setup(x => x.GenerateImageAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test error"));

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.StatusMessage.Should().Be("Error: Test error");
        _viewModel.StatusKind.Should().Be(StatusKind.Error);
        _viewModel.GeneratedImagePath.Should().BeNull();
        _viewModel.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateImage_WhenServiceReturnsCanceledMessage_ShouldSetCanceledKind()
    {
        _viewModel.Parameters.ApiToken = "valid-token";
        _viewModel.Parameters.Prompt = "test prompt";
        _mockImageService
            .Setup(x => x.GenerateImageAsync(It.IsAny<ImageGenerationParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedImage { Message = "Image generation was canceled.", ImageDataBase64 = null });

        await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand).ExecuteAsync(null);

        _viewModel.StatusKind.Should().Be(StatusKind.Canceled);
        _viewModel.GeneratedImagePath.Should().BeNull();
    }
}
