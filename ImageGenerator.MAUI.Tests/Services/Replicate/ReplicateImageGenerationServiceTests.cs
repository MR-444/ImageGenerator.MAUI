using FluentAssertions;
using Moq;
using Moq.Protected;
using System.Net;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Models.Replicate;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Services.Replicate;

public class ReplicateImageGenerationServiceTests
{
    private readonly Mock<IReplicateApi> _mockReplicateApi;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly ReplicateImageGenerationService _service;

    public ReplicateImageGenerationServiceTests()
    {
        _mockReplicateApi = new Mock<IReplicateApi>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _service = new ReplicateImageGenerationService(_mockReplicateApi.Object, httpClient);
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

    // Simplify the mock setup to use It.IsAny to avoid complex matching issues
    _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<ReplicatePredictionRequest>()))
        .ReturnsAsync(initialResponse);

    _mockReplicateApi.Setup(x => x.GetPredictionAsync(
        It.IsAny<string>(),
        It.IsAny<string>()))
        .ReturnsAsync(finalResponse);

    // Mock the HttpClient to return a successful response with image data
    _mockHttpMessageHandler
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        )
        .ReturnsAsync(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new ByteArrayContent([1, 2, 3]) // Mock image data
        });

    // Act
    var result = await _service.GenerateImageAsync(parameters);

    // Assert
    result.Should().NotBeNull();
    result.Message.Should().Be($"Image generated successfully with model {parameters.Model}.");
    result.ImageDataBase64.Should().NotBeNull();
    
    // Verify the input image formatting - expect PNG since DetectImageMimeType will default to PNG for "test-base64-data"
    _mockReplicateApi.Verify(x => x.CreatePredictionAsync(
        It.Is<string>(token => token == "Bearer test-token"),
        It.Is<string>(model => model == parameters.Model),
        It.Is<ReplicatePredictionRequest>(req => 
            req.Input.GetType() == typeof(FluxKontextMax) &&
            ((FluxKontextMax)req.Input).InputImage == "data:image/png;base64,test-base64-data"
        )), Times.Once);
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

        // Mock the HttpClient to return a successful response with image data
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ByteArrayContent([1, 2, 3]) // Mock image data
            });

        // Act
        var result = await _service.GenerateImageAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Message.Should().Be($"Image generated successfully with model {parameters.Model}.");
        result.ImageDataBase64.Should().NotBeNull();
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
        result.Message.Should().Be("An error occurred: Model prediction failed or returned no result. Status: succeeded, Error: Unknown error");
        result.ImageDataBase64.Should().BeNull();
        result.FilePath.Should().BeNull();
    }
}