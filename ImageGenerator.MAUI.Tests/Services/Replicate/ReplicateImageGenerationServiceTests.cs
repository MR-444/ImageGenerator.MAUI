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
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextMax,
            Prompt = "A test image",
            ApiToken = "test-token",
            ImagePrompt = "test-base64-data"
        };

        var initialResponse = new ReplicatePredictionResponse { Id = "test-id", Status = "starting" };
        var finalResponse = new ReplicatePredictionResponse { Status = "succeeded", Output = new[] { "https://example.com/image.jpg" } };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ReplicatePredictionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialResponse);

        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(finalResponse);

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ByteArrayContent([1, 2, 3])
            });

        var result = await _service.GenerateImageAsync(parameters);

        result.Should().NotBeNull();
        result.Message.Should().Be($"Image generated successfully with model {parameters.Model}.");
        result.ImageDataBase64.Should().NotBeNull();

        _mockReplicateApi.Verify(x => x.CreatePredictionAsync(
            It.Is<string>(token => token == "Bearer test-token"),
            It.Is<string>(model => model == parameters.Model),
            It.Is<ReplicatePredictionRequest>(req =>
                req.Input.GetType() == typeof(FluxKontextMax) &&
                ((FluxKontextMax)req.Input).InputImage == "data:image/png;base64,test-base64-data"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateImageAsync_WithNoImagePrompt_ShouldNotModifyInputImage()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A test image",
            ApiToken = "test-token",
            ImagePrompt = null
        };

        var initialResponse = new ReplicatePredictionResponse { Id = "test-id", Status = "starting" };
        var finalResponse = new ReplicatePredictionResponse { Status = "succeeded", Output = new[] { "https://example.com/image.jpg" } };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
                It.Is<string>(token => token == "Bearer test-token"),
                It.Is<string>(model => model == parameters.Model),
                It.Is<ReplicatePredictionRequest>(req =>
                    req.Input.GetType() == typeof(FluxKontextPro) &&
                    ((FluxKontextPro)req.Input).InputImage == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialResponse);

        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
                It.Is<string>(token => token == "Bearer test-token"),
                It.Is<string>(id => id == "test-id"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(finalResponse);

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ByteArrayContent([1, 2, 3])
            });

        var result = await _service.GenerateImageAsync(parameters);

        result.Should().NotBeNull();
        result.Message.Should().Be($"Image generated successfully with model {parameters.Model}.");
        result.ImageDataBase64.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WhenApiCallFails_ShouldReturnErrorResult()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ReplicatePredictionRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API Error"));

        var result = await _service.GenerateImageAsync(parameters);

        result.Should().NotBeNull();
        result.Message.Should().Be("An error occurred: API Error");
        result.ImageDataBase64.Should().BeNull();
        result.FilePath.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WhenApiReturnsNoOutput_ShouldReturnErrorResult()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        var initialResponse = new ReplicatePredictionResponse { Id = "test-id", Status = "starting" };
        var emptyResponse = new ReplicatePredictionResponse { Status = "succeeded", Output = null };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ReplicatePredictionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialResponse);

        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyResponse);

        var result = await _service.GenerateImageAsync(parameters);

        result.Should().NotBeNull();
        result.Message.Should().Be("An error occurred: Model prediction failed or returned no result. Status: succeeded, Error: Unknown error");
        result.ImageDataBase64.Should().BeNull();
        result.FilePath.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WhenCanceled_ShouldReturnCanceledResult()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ReplicatePredictionRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await _service.GenerateImageAsync(parameters, CancellationToken.None);

        result.Should().NotBeNull();
        result.Message.Should().Be("Image generation was canceled.");
        result.ImageDataBase64.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WhenStatusUnknown_ShouldReturnErrorResult()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.KontextPro,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        var initialResponse = new ReplicatePredictionResponse { Id = "test-id", Status = "starting" };
        var unknownStatus = new ReplicatePredictionResponse { Status = "definitely-not-a-real-status", Output = null };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ReplicatePredictionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialResponse);

        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(unknownStatus);

        var result = await _service.GenerateImageAsync(parameters);

        result.Should().NotBeNull();
        result.Message.Should().StartWith("An error occurred: Unexpected Replicate prediction status");
        result.ImageDataBase64.Should().BeNull();
    }
}
