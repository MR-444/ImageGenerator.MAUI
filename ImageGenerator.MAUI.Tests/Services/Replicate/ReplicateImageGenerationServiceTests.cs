using FluentAssertions;
using Moq;
using Moq.Protected;
using System.Net;
using ImageGenerator.MAUI.Core.Domain.Entities;
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
    public async Task GenerateImageAsync_Flux2WithImagePrompt_EmbedsDataUriIntoImagesArray()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Klein4b,
            Prompt = "A test image",
            ApiToken = "test-token"
        };
        parameters.ImagePrompts.Add("test-base64-data");

        StubHappyPath();

        await _service.GenerateImageAsync(parameters);

        _mockReplicateApi.Verify(x => x.CreatePredictionAsync(
            "Bearer test-token",
            parameters.Model,
            It.Is<ReplicatePredictionRequest>(req =>
                HasImagesArrayWithUri(req, "data:image/png;base64,test-base64-data")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateImageAsync_Flux2WithoutImagePrompt_LeavesImagesKeyNull()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Klein4b,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        StubHappyPath();

        await _service.GenerateImageAsync(parameters);

        _mockReplicateApi.Verify(x => x.CreatePredictionAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.Is<ReplicatePredictionRequest>(req => HasNullImages(req)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static bool HasImagesArrayWithUri(ReplicatePredictionRequest req, string uri)
    {
        var dict = (Dictionary<string, object?>)req.Input;
        var images = dict["images"] as string[];
        return images != null && images.Length == 1 && images[0] == uri;
    }

    private static bool HasNullImages(ReplicatePredictionRequest req)
    {
        var dict = (Dictionary<string, object?>)req.Input;
        return dict["images"] == null;
    }

    [Fact]
    public async Task GenerateImageAsync_WhenApiCallFails_ShouldReturnErrorResult()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Klein4b,
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
            Model = ModelConstants.Flux.Klein4b,
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
            Model = ModelConstants.Flux.Klein4b,
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
            Model = ModelConstants.Flux.Klein4b,
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

    private void StubHappyPath()
    {
        var initialResponse = new ReplicatePredictionResponse { Id = "test-id", Status = "starting" };
        var finalResponse = new ReplicatePredictionResponse
        {
            Status = "succeeded",
            Output = new[] { "https://example.com/image.jpg" }
        };

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
    }
}
