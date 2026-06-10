using System.Net;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.External.Pollinations;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Models.Replicate;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ImageGenerator.MAUI.Tests.Services;

public class ImageGenerationDispatcherTests
{
    private readonly Mock<IReplicateApi> _replicateApi = new();
    private readonly Mock<HttpMessageHandler> _pollinationsHandler = new(MockBehavior.Strict);
    private readonly Mock<HttpMessageHandler> _replicateHandler = new(MockBehavior.Strict);
    private readonly Mock<HttpMessageHandler> _comfyHandler = new(MockBehavior.Loose);
    private readonly List<HttpRequestMessage> _pollinationsRequests = new();
    private readonly List<HttpRequestMessage> _comfyRequests = new();
    private readonly ImageGenerationDispatcher _dispatcher;

    public ImageGenerationDispatcherTests()
    {
        var registry = ModelDescriptorRegistry.Default();

        var replicate = new ReplicateImageGenerationService(
            _replicateApi.Object,
            new StubHttpClientFactory(new HttpClient(_replicateHandler.Object)),
            registry,
            NullLogger<ReplicateImageGenerationService>.Instance);

        var pollinations = new PollinationsImageGenerationService(
            new StubHttpClientFactory(new HttpClient(_pollinationsHandler.Object)),
            registry,
            NullLogger<PollinationsImageGenerationService>.Instance);

        _comfyHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _comfyRequests.Add(req))
            .ReturnsAsync(() => new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound });

        var comfyUi = new ComfyUiImageGenerationService(
            new StubHttpClientFactory(new HttpClient(_comfyHandler.Object)),
            registry,
            Mock.Of<IUiStateStore>(),
            NullLogger<ComfyUiImageGenerationService>.Instance,
            workflowsDirectoryOverride: Path.Combine(Path.GetTempPath(), "imggen-dispatcher-tests-empty"));

        _dispatcher = new ImageGenerationDispatcher(replicate, pollinations, comfyUi);
    }

    [Fact]
    public async Task GenerateImageAsync_ComfyUiModelPrefix_RoutesToComfyService()
    {
        // The workflow file doesn't exist, so the ComfyUI service fails fast with its own
        // message — proving the route without needing a full HTTP choreography here.
        var parameters = new ImageGenerationParameters
        {
            Model = "comfyui/some workflow",
            Prompt = "a cat"
        };

        var result = await _dispatcher.GenerateImageAsync(parameters);

        result.Message.Should().Contain("Export it from ComfyUI",
            "the ComfyUI service must handle comfyui/* ids");
        _pollinationsRequests.Should().BeEmpty();
        _replicateApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateImageAsync_PollinationsModelPrefix_RoutesToPollinationsService()
    {
        StubPollinationsOk();

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            Seed = 12345
        };

        await _dispatcher.GenerateImageAsync(parameters);

        _pollinationsRequests.Should().ContainSingle(
            "Pollinations handler must receive the call when model has 'pollinations/' prefix");
        _replicateApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateImageAsync_NonPollinationsModel_RoutesToReplicateService()
    {
        StubReplicateOk();

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Flux.Klein4b,
            Prompt = "a cat",
            ApiToken = "test-token",
            Seed = 12345
        };

        await _dispatcher.GenerateImageAsync(parameters);

        _replicateApi.Verify(x => x.CreatePredictionAsync(
            It.IsAny<string>(),
            It.Is<string>(m => m == ModelConstants.Flux.Klein4b),
            It.IsAny<ReplicatePredictionRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _pollinationsRequests.Should().BeEmpty(
            "Pollinations handler must not be touched for a Replicate-shaped model id");
    }

    [Fact]
    public async Task GenerateImageAsync_PollinationsModel_DoesNotMutateCallerParametersSeed()
    {
        // F1 regression: dispatcher must not clamp parameters.Seed in place. Pick a seed above
        // int.MaxValue so the legacy `parameters.Seed &= int.MaxValue` path would have changed it.
        StubPollinationsOk();

        const long seedAboveInt32 = (long)int.MaxValue + 5;
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            Seed = seedAboveInt32
        };

        await _dispatcher.GenerateImageAsync(parameters);

        parameters.Seed.Should().Be(seedAboveInt32,
            "dispatcher must leave caller's parameters untouched (seed clamp now lives at the wire boundary)");
    }

    [Fact]
    public async Task GenerateImageAsync_EmptyModelString_RoutesToReplicate()
    {
        // IsPollinations guards against null/empty, so empty model falls through to Replicate.
        // Replicate's registry throws on unknown model, which the service catches and turns
        // into a failure result. Routing is what we're pinning here.
        var parameters = new ImageGenerationParameters
        {
            Model = string.Empty,
            Prompt = "a cat",
            ApiToken = "test-token"
        };

        var result = await _dispatcher.GenerateImageAsync(parameters);

        result.ImageData.Should().BeNull();
        _pollinationsRequests.Should().BeEmpty(
            "empty model id must not be treated as Pollinations");
    }

    private void StubPollinationsOk()
    {
        _pollinationsHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _pollinationsRequests.Add(req))
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                // Must exceed MinValidImageBytes (1000) so the Pollinations service accepts it.
                Content = new ByteArrayContent(new byte[2048])
            });
    }

    private void StubReplicateOk()
    {
        _replicateApi.Setup(x => x.CreatePredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ReplicatePredictionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplicatePredictionResponse { Id = "id", Status = "starting" });

        _replicateApi.Setup(x => x.GetPredictionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplicatePredictionResponse
            {
                Status = "succeeded",
                Output = new[] { "https://example.com/image.jpg" }
            });

        _replicateHandler
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
