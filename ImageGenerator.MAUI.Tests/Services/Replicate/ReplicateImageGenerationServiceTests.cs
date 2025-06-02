using FluentAssertions;
using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Models.Flux;
using ImageGenerator.MAUI.Models.Replicate;
using ImageGenerator.MAUI.Services.Replicate;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services.Replicate;

public class ReplicateImageGenerationServiceTests
{
    private readonly Mock<IReplicateApi> _mockReplicateApi;
    private readonly ReplicateImageGenerationService _service;

    public ReplicateImageGenerationServiceTests()
    {
        _mockReplicateApi = new Mock<IReplicateApi>();
        _service = new ReplicateImageGenerationService(_mockReplicateApi.Object);
    }

    [Fact]
    public async Task GenerateImageAsync_WithFluxKontextPro_ShouldFormatInputImageCorrectly()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A test image",
            ApiToken = "test-token",
            ImagePrompt = "test-base64-data"
        };

        var initialResponse = new ReplicatePredictionResponse
        {
            Id = "test-id",
            Status = "starting"
        };

        var finalResponse = new ReplicatePredictionResponse
        {
            Status = "succeeded",
            Output = "https://example.com/image.jpg"
        };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
            It.Is<string>(token => token == "Bearer test-token"),
            It.Is<string>(model => model == parameters.Model),
            It.Is<ReplicatePredictionRequest>(req => 
                req.Input.GetType() == typeof(FluxKontextPro) &&
                ((FluxKontextPro)req.Input).InputImage == "data:image/jpeg;base64,test-base64-data"
            )))
            .ReturnsAsync(initialResponse);

        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
            It.Is<string>(token => token == "Bearer test-token"),
            It.Is<string>(id => id == "test-id")))
            .ReturnsAsync(finalResponse);

        // Act
        var result = await _service.GenerateImageAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Message.Should().Be($"Image generated successfully with model {parameters.Model}.");
    }

    [Fact]
    public async Task GenerateImageAsync_WithFluxKontextMax_ShouldFormatInputImageCorrectly()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextMax,
            Prompt = "A test image",
            ApiToken = "test-token",
            ImagePrompt = "test-base64-data"
        };

        var initialResponse = new ReplicatePredictionResponse
        {
            Id = "test-id",
            Status = "starting"
        };

        var finalResponse = new ReplicatePredictionResponse
        {
            Status = "succeeded",
            Output = "https://example.com/image.jpg"
        };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
            It.Is<string>(token => token == "Bearer test-token"),
            It.Is<string>(model => model == parameters.Model),
            It.Is<ReplicatePredictionRequest>(req => 
                req.Input.GetType() == typeof(FluxKontextMax) &&
                ((FluxKontextMax)req.Input).InputImage == "data:image/jpeg;base64,test-base64-data"
            )))
            .ReturnsAsync(initialResponse);

        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
            It.Is<string>(token => token == "Bearer test-token"),
            It.Is<string>(id => id == "test-id")))
            .ReturnsAsync(finalResponse);

        // Act
        var result = await _service.GenerateImageAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Message.Should().Be($"Image generated successfully with model {parameters.Model}.");
    }

    [Fact]
    public async Task GenerateImageAsync_WithNoImagePrompt_ShouldNotModifyInputImage()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A test image",
            ApiToken = "test-token",
            ImagePrompt = null
        };

        var initialResponse = new ReplicatePredictionResponse
        {
            Id = "test-id",
            Status = "starting"
        };

        var finalResponse = new ReplicatePredictionResponse
        {
            Status = "succeeded",
            Output = "https://example.com/image.jpg"
        };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
            It.Is<string>(token => token == "Bearer test-token"),
            It.Is<string>(model => model == parameters.Model),
            It.Is<ReplicatePredictionRequest>(req => 
                req.Input.GetType() == typeof(FluxKontextPro) &&
                ((FluxKontextPro)req.Input).InputImage == null
            )))
            .ReturnsAsync(initialResponse);

        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
            It.Is<string>(token => token == "Bearer test-token"),
            It.Is<string>(id => id == "test-id")))
            .ReturnsAsync(finalResponse);

        // Act
        var result = await _service.GenerateImageAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Message.Should().Be($"Image generated successfully with model {parameters.Model}.");
    }

    [Fact]
    public async Task GenerateImageAsync_WhenApiCallFails_ShouldReturnErrorResult()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ReplicatePredictionRequest>()))
            .ThrowsAsync(new Exception("API Error"));

        // Act
        var result = await _service.GenerateImageAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Message.Should().Be("An error occurred: API Error");
        result.ImageDataBase64.Should().BeNull();
        result.FilePath.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WhenApiReturnsNoOutput_ShouldReturnErrorResult()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        var initialResponse = new ReplicatePredictionResponse
        {
            Id = "test-id",
            Status = "starting"
        };

        var emptyResponse = new ReplicatePredictionResponse
        {
            Status = "succeeded",
            Output = null
        };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ReplicatePredictionRequest>()))
            .ReturnsAsync(initialResponse);

        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
            It.IsAny<string>(),
            It.IsAny<string>()))
            .ReturnsAsync(emptyResponse);

        // Act
        var result = await _service.GenerateImageAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Message.Should().Be("An error occurred: ModelName prediction failed or returned no result.");
        result.ImageDataBase64.Should().BeNull();
        result.FilePath.Should().BeNull();
    }
} 