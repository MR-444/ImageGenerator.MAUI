using FluentAssertions;
using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.ViewModels;
using Moq;
using ImageGenerator.MAUI.Services;
using ImageGenerator.MAUI.Models;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using Image = SixLabors.ImageSharp.Image;
using CommunityToolkit.Mvvm.Input;

namespace ImageGenerator.MAUI.Tests.ViewModels
{
    public class GeneratorViewModelTests
    {
        private readonly GeneratorViewModel _viewModel;
        private readonly Mock<IImageGenerationService> _mockImageService;
        private readonly Mock<IFileSystem> _mockFileSystem;

        public GeneratorViewModelTests()
        {
            _mockImageService = new Mock<IImageGenerationService>();
            _mockFileSystem = new Mock<IFileSystem>();
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
            var expectedImageData = "base64ImageData";
            var expectedMessage = "Success";
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
        public void UpdateCustomAspectRatio_WhenCustomSelected_ShouldClampDimensions()
        {
            // Arrange
            _viewModel.Parameters.AspectRatio = "custom";
            _viewModel.Parameters.Width = 100; // Below minimum
            _viewModel.Parameters.Height = 5000; // Above maximum

            // Assert
            _viewModel.Parameters.Width.Should().Be(ValidationConstants.ImageWidthMin);
            _viewModel.Parameters.Height.Should().Be(ValidationConstants.ImageHeightMax);
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

        [Fact]
        public async Task GenerateImage_ShouldSaveImageWithCorrectMetadata()
        {
            // Arrange
            var expectedImageData = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
            var expectedMessage = "Success";
            _viewModel.Parameters.ApiToken = "valid-token";
            _viewModel.Parameters.Prompt = "test prompt";
            _viewModel.Parameters.Model = "test-model";
            _viewModel.Parameters.Seed = 123;
            _viewModel.Parameters.AspectRatio = "16:9";
            _viewModel.Parameters.Width = 1920;
            _viewModel.Parameters.Height = 1080;
            _viewModel.Parameters.OutputFormat = ImageOutputFormat.Png;
            _viewModel.Parameters.OutputQuality = 100;
            _viewModel.Parameters.PromptUpsampling = true;

            _mockImageService
                .Setup(x => x.GenerateImageAsync(It.IsAny<ImageGenerationParameters>()))
                .ReturnsAsync(new GeneratedImage { ImageDataBase64 = expectedImageData, Message = expectedMessage });

            // Act
            await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand!).ExecuteAsync(null);

            // Assert
            _viewModel.GeneratedImagePath.Should().NotBeNull();
            if (_viewModel.GeneratedImagePath != null)
            {
                File.Exists(_viewModel.GeneratedImagePath).Should().BeTrue();
                using (var savedImage = await Image.LoadAsync<Rgba32>(_viewModel.GeneratedImagePath))
                {
                    savedImage.Metadata.ExifProfile.Should().NotBeNull();
                    var userComment = savedImage.Metadata.ExifProfile.Values
                        .FirstOrDefault(x => x.Tag == ExifTag.UserComment)
                        ?.GetValue() as string;
                    userComment.Should().NotBeNull();
                    userComment.Should().Contain("test prompt");
                    userComment.Should().Contain("test-model");
                    userComment.Should().Contain("123");
                    userComment.Should().Contain("16:9");
                    userComment.Should().Contain("1920x1080");
                }

                // Cleanup
                File.Delete(_viewModel.GeneratedImagePath);
            }
        }

        [Theory]
        [InlineData(ImageOutputFormat.Jpg, 90)]
        [InlineData(ImageOutputFormat.Webp, 80)]
        [InlineData(ImageOutputFormat.Png, 100)]
        public async Task GenerateImage_ShouldUseCorrectEncoder(ImageOutputFormat format, int quality)
        {
            // Arrange
            var expectedImageData = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
            var expectedMessage = "Success";
            _viewModel.Parameters.ApiToken = "valid-token";
            _viewModel.Parameters.OutputFormat = format;
            _viewModel.Parameters.OutputQuality = quality;

            _mockImageService
                .Setup(x => x.GenerateImageAsync(It.IsAny<ImageGenerationParameters>()))
                .ReturnsAsync(new GeneratedImage { ImageDataBase64 = expectedImageData, Message = expectedMessage });

            // Act
            await ((IAsyncRelayCommand)_viewModel.GenerateImageCommand!).ExecuteAsync(null);

            // Assert
            _viewModel.GeneratedImagePath.Should().NotBeNull();
            if (_viewModel.GeneratedImagePath != null)
            {
                File.Exists(_viewModel.GeneratedImagePath).Should().BeTrue();
                var fileInfo = new FileInfo(_viewModel.GeneratedImagePath);
                fileInfo.Extension.Should().Be($".{format.ToString().ToLower()}");

                // Cleanup
                File.Delete(_viewModel.GeneratedImagePath);
            }
        }
    }
} 