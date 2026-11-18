using FluentAssertions;
using Moq;
using Moq.Protected;
using System.Net;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

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
        _service = new ReplicateImageGenerationService(
            _mockReplicateApi.Object,
            new StubHttpClientFactory(httpClient),
            ModelDescriptorRegistry.Default(),
            NullLogger<ReplicateImageGenerationService>.Instance);
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

    /// <summary>
    /// A rejected payload is the one failure whose fix Replicate writes down, and it writes it in
    /// the response body. The label has to carry the field and the accepted values, not the JSON.
    /// </summary>
    [Fact]
    public async Task GenerateImageAsync_InputRejected_SurfacesTheFieldInsteadOfTheBody()
    {
        const string body =
            """
            {"detail":"- input.aspect_ratio: aspect_ratio must be one of the following: \"1:1\", \"3:2\"\n","status":422,"title":"Input validation failed","invalid_fields":[{"type":"enum","field":"input.aspect_ratio","description":"aspect_ratio must be one of the following: \"1:1\", \"3:2\""}]}
            """;
        _mockReplicateApi
            .Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ReplicatePredictionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(await Rejected(422, body));

        var result = await _service.GenerateImageAsync(new ImageGenerationParameters
        {
            Model = "openai/gpt-image-2.6-sunburst",
            Prompt = "x",
            ApiToken = "test-token"
        });

        result.ImageData.Should().BeNull();
        result.Message.Should().NotBeNull();
        result.Message.Should().StartWith("HTTP 422: Input validation failed:")
            .And.Contain("aspect_ratio").And.Contain(@"""1:1""");
        // The whole point: the status label stops being a JSON dump.
        result.Message.Should().NotContain("invalid_fields");
    }

    /// <summary>The token never reaches the body today; it must still never come back out of the
    /// summarising path, which rebuilds the message from server-controlled text.</summary>
    [Fact]
    public async Task GenerateImageAsync_InputRejectedWithTokenInBody_RedactsTheToken()
    {
        const string body =
            """
            {"status":422,"title":"Input validation failed","invalid_fields":[{"field":"input.prompt","description":"contains a credential r8_abc123secret"}]}
            """;
        _mockReplicateApi
            .Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ReplicatePredictionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(await Rejected(422, body));

        var result = await _service.GenerateImageAsync(new ImageGenerationParameters
        {
            Model = "openai/gpt-image-2.6-sunburst",
            Prompt = "x",
            ApiToken = "r8_abc123secret"
        });

        result.Message.Should().NotBeNull().And.NotContain("r8_abc123secret").And.Contain("[REDACTED]");
    }

    /// <summary>Builds the exception Refit throws for a non-2xx reply, body and all. Refit's own
    /// factory is the only public way to one — every ApiException constructor is protected.</summary>
    private static async Task<Refit.ApiException> Rejected(int statusCode, string body) =>
        await Refit.ApiException.Create(
            new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/models/openai/predictions"),
            HttpMethod.Post,
            new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            },
            new Refit.RefitSettings());

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

    private static Dictionary<string, object?> InputOf(ReplicatePredictionRequest req) =>
        (Dictionary<string, object?>)req.Input;

    /// <summary>
    /// The bug, end to end. openai/gpt-image-2.6-sunburst reaches the picker through the catalog
    /// owner allow-list; with no descriptor of its own it fell through to the generic Replicate
    /// payload, which sent it "images"/"output_quality"/"seed" — three fields it never declared,
    /// silently dropped since replicate/cog#2943 — plus an aspect_ratio from a list it rejects,
    /// which is the HTTP 422 on input.aspect_ratio in documents/HTTP Logs.
    /// </summary>
    [Fact]
    public async Task GenerateImageAsync_GptImage26Sunburst_SendsItsOwnSchemaFieldSet()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.OpenAI.GptImage26SunburstOnReplicate,
            Prompt = "A test image",
            ApiToken = "test-token",
            AspectRatio = "1024x1024",
            GptQuality = "xhigh",
            GptComplexity = "low"
        };
        parameters.ImagePrompts.Add("test-base64-data");

        StubHappyPath();

        await _service.GenerateImageAsync(parameters);

        _mockReplicateApi.Verify(x => x.CreatePredictionAsync(
            "Bearer test-token",
            parameters.Model,
            It.Is<ReplicatePredictionRequest>(req => HasGptImage26Input(InputOf(req))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static bool HasGptImage26Input(Dictionary<string, object?> input)
        => input["aspect_ratio"] as string == "1024x1024"
            && input["quality"] as string == "xhigh"
            && input["complexity"] as string == "low"
            // The name this model actually declares, unlike the fallback's "images"…
            && (input["input_images"] as string[])?.Length == 1
            // …and three it never had.
            && !input.ContainsKey("images")
            && !input.ContainsKey("output_quality")
            && !input.ContainsKey("seed")
            && !input.ContainsKey("input_fidelity");

    [Fact]
    public async Task GenerateImageAsync_GptImage15_KeepsInputFidelityAndSendsNoComplexity()
    {
        // The other half of the per-model profile: the new field must not leak back into the model
        // that has shipped with this exact payload all along.
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.OpenAI.GptImage15OnReplicate,
            Prompt = "A test image",
            ApiToken = "test-token",
            AspectRatio = "1:1",
            GptInputFidelity = "high"
        };

        StubHappyPath();

        await _service.GenerateImageAsync(parameters);

        _mockReplicateApi.Verify(x => x.CreatePredictionAsync(
            "Bearer test-token",
            parameters.Model,
            It.Is<ReplicatePredictionRequest>(req =>
                InputOf(req)["input_fidelity"] as string == "high"
                && !InputOf(req).ContainsKey("complexity")),
            It.IsAny<CancellationToken>()), Times.Once);
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
        result.ImageData.Should().BeNull();
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
        result.Message.Should().Be("An error occurred: Model prediction returned no output. Status: succeeded, Error: Unknown error");
        result.ImageData.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WhenStatusFailedWithError_ShouldSurfaceCleanFailedMessage()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Klein4b,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        var initialResponse = new ReplicatePredictionResponse { Id = "test-id", Status = "starting" };
        var failedResponse = new ReplicatePredictionResponse { Status = "failed", Error = "model went boom" };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ReplicatePredictionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialResponse);
        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResponse);

        var result = await _service.GenerateImageAsync(parameters);

        result.Should().NotBeNull();
        result.Message.Should().Be("Image generation failed: model went boom.");
        result.ImageData.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WhenStatusFailedWithoutError_ShouldSurfaceFailedMessageWithFallback()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Klein4b,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        var initialResponse = new ReplicatePredictionResponse { Id = "test-id", Status = "starting" };
        var failedResponse = new ReplicatePredictionResponse { Status = "failed", Error = null };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ReplicatePredictionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialResponse);
        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResponse);

        var result = await _service.GenerateImageAsync(parameters);

        result.Message.Should().Be("Image generation failed: no error message.");
        result.ImageData.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WhenStatusCanceledServerSide_ShouldSurfaceCanceledMessage()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Klein4b,
            Prompt = "A test image",
            ApiToken = "test-token"
        };

        var initialResponse = new ReplicatePredictionResponse { Id = "test-id", Status = "starting" };
        var canceledResponse = new ReplicatePredictionResponse { Status = "canceled" };

        _mockReplicateApi.Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ReplicatePredictionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialResponse);
        _mockReplicateApi.Setup(x => x.GetPredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(canceledResponse);

        var result = await _service.GenerateImageAsync(parameters);

        result.Message.Should().Be("Image generation was canceled.");
        result.ImageData.Should().BeNull();
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
        result.ImageData.Should().BeNull();
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
        result.ImageData.Should().BeNull();
    }

    private void StubHappyPath()
    {
        var initialResponse = new ReplicatePredictionResponse { Id = "test-id", Status = "starting" };
        var finalResponse = new ReplicatePredictionResponse
        {
            Status = "succeeded",
            Output = ["https://example.com/image.jpg"]
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
