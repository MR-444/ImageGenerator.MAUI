using FluentAssertions;
using ImageGenerator.MAUI.Common;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Models.OpenAi;
using ImageGenerator.MAUI.Services.OpenAi;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services.OpenAi;

public class OpenAiImageGenerationServiceTests
{
    private readonly Mock<IOpenAiApi> _mockOpenAiApi;
    private readonly OpenAiImageGenerationService _service;

    public OpenAiImageGenerationServiceTests()
    {
        _mockOpenAiApi = new Mock<IOpenAiApi>();
        _service = new OpenAiImageGenerationService(_mockOpenAiApi.Object);
    }

    [Fact]
    public async Task GenerateImageAsync_ShouldReturnSuccessResult_WhenApiCallSucceeds()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.OpenAI.GptImage1,
            Prompt = "A test image",
            ApiToken = "test-token",
            Width = 1024,
            Height = 1024,
            OutputFormat = ImageOutputFormat.Png,
            OutputQuality = 90
        };

        var expectedResponse = new OpenAiResponse
        {
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Data = new List<ImageData>
            {
                new() { B64Json = "test-base64-data" }
            },
            Usage = new UsageInfo
            {
                TotalTokens = 100,
                InputTokens = 50,
                OutputTokens = 50,
                InputTokensDetails = new InputTokensDetails
                {
                    TextTokens = 50,
                    ImageTokens = 0
                }
            }
        };

        _mockOpenAiApi.Setup(x => x.CreatePredictionAsync(
            It.Is<string>(token => token == "Bearer test-token"),
            It.Is<OpenAiRequest>(req => 
                req.Prompt == parameters.Prompt &&
                req.ModelName == parameters.Model &&
                req.Size == "1024x1024" &&
                req.OutputFormat == "png" &&
                req.OutputCompression == 90 &&
                req.ResponseFormat == "b64_json"
            )))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.GenerateImageAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Message.Should().Be("Image generated successfully with OpenAI.");
        result.ImageDataBase64.Should().Be("test-base64-data");
        result.FilePath.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_ShouldReturnErrorResult_WhenApiCallFails()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.OpenAI.GptImage1,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        _mockOpenAiApi.Setup(x => x.CreatePredictionAsync(
            It.IsAny<string>(),
            It.IsAny<OpenAiRequest>()))
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
    public async Task GenerateImageAsync_ShouldReturnErrorResult_WhenApiReturnsNoData()
    {
        // Arrange
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.OpenAI.GptImage1,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        var emptyResponse = new OpenAiResponse
        {
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Data = new List<ImageData>(),
            Usage = new UsageInfo
            {
                TotalTokens = 0,
                InputTokens = 0,
                OutputTokens = 0,
                InputTokensDetails = new InputTokensDetails
                {
                    TextTokens = 0,
                    ImageTokens = 0
                }
            }
        };

        _mockOpenAiApi.Setup(x => x.CreatePredictionAsync(
            It.IsAny<string>(),
            It.IsAny<OpenAiRequest>()))
            .ReturnsAsync(emptyResponse);

        // Act
        var result = await _service.GenerateImageAsync(parameters);

        // Assert
        result.Should().NotBeNull();
        result.Message.Should().Be("An error occurred: OpenAI image generation failed or returned no result.");
        result.ImageDataBase64.Should().BeNull();
        result.FilePath.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenApiIsNull()
    {
        // Act
        var act = () => new OpenAiImageGenerationService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("openAiApi");
    }
} 