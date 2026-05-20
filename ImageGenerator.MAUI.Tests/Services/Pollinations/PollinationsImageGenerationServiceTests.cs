using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Web;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.Pollinations;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ImageGenerator.MAUI.Tests.Services.Pollinations;

public class PollinationsImageGenerationServiceTests
{
    // Loose mode is required because the SUT uses `using var httpClient = ...`, which calls
    // HttpClient.Dispose → handler.Dispose at end of scope. Strict mode would throw on the
    // unconfigured Dispose call and surface as a generic error in result.Message, masking
    // every real assertion.
    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Loose);
    private readonly List<HttpRequestMessage> _requests = new();
    private readonly PollinationsImageGenerationService _service;

    // Per-test response factory. Default is a happy-path 200 with 2048 bytes (matches the
    // service's MinValidImageBytes=1000 floor with margin). Tests that need a different
    // response (4xx/5xx, undersized, cancellation) reassign this in Arrange.
    private Func<HttpResponseMessage> _nextResponse =
        () => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new ByteArrayContent(new byte[2048])
        };

    public PollinationsImageGenerationServiceTests()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _requests.Add(req))
            .ReturnsAsync(() => _nextResponse());

        _service = new PollinationsImageGenerationService(
            new StubHttpClientFactory(new HttpClient(_handler.Object)),
            ModelDescriptorRegistry.Default(),
            NullLogger<PollinationsImageGenerationService>.Instance);
    }

    [Fact]
    public async Task GenerateImageAsync_SeedAboveInt32Max_SendsClampedSeedOnWire()
    {
        // Pollinations rejects seeds above int.MaxValue with HTTP 400. The clamp at the wire
        // boundary keeps the entity untouched (so metadata reflects the user's seed) while the
        // URL contains the masked-down int32 value.
        const long seedAboveInt32 = (long)int.MaxValue + 5;
        const long expectedClamped = seedAboveInt32 & int.MaxValue;

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            Seed = seedAboveInt32
        };

        await _service.GenerateImageAsync(parameters);

        ExtractSeedFromCapturedUrl().Should().Be(expectedClamped);
        parameters.Seed.Should().Be(seedAboveInt32, "service must not mutate the caller's seed");
    }

    [Fact]
    public async Task GenerateImageAsync_SeedWithinInt32Range_SendsSeedUnchanged()
    {
        const long inRangeSeed = 12345;

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            Seed = inRangeSeed
        };

        await _service.GenerateImageAsync(parameters);

        ExtractSeedFromCapturedUrl().Should().Be(inRangeSeed);
    }

    [Fact]
    public async Task GenerateImageAsync_SafeTrue_AppendsSafeNsfwQueryParam()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            Safe = true
        };

        await _service.GenerateImageAsync(parameters);

        ExtractQueryParam("safe").Should().Be("nsfw");
    }

    [Fact]
    public async Task GenerateImageAsync_SafeFalse_OmitsSafeQueryParam()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            Safe = false
        };

        await _service.GenerateImageAsync(parameters);

        ExtractQueryParam("safe").Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WithToken_AddsBearerAuthorizationHeader()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            PollinationsApiToken = "tok-abc-123"
        };

        await _service.GenerateImageAsync(parameters);

        _requests.Should().ContainSingle();
        var auth = _requests[0].Headers.Authorization;
        auth.Should().NotBeNull();
        auth!.Scheme.Should().Be("Bearer");
        auth.Parameter.Should().Be("tok-abc-123");
    }

    [Fact]
    public async Task GenerateImageAsync_WithoutToken_OmitsAuthorizationHeader()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            PollinationsApiToken = string.Empty
        };

        await _service.GenerateImageAsync(parameters);

        _requests.Should().ContainSingle();
        _requests[0].Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_WhitespaceToken_OmitsAuthorizationHeader()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            PollinationsApiToken = "   "
        };

        await _service.GenerateImageAsync(parameters);

        _requests.Should().ContainSingle();
        _requests[0].Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task GenerateImageAsync_Http400WithBody_ReturnsMessageContainingStatusAndBody()
    {
        _nextResponse = () => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.BadRequest,
            Content = new StringContent("invalid model 'foo'")
        };

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat"
        };

        var result = await _service.GenerateImageAsync(parameters);

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("HTTP 400");
        result.Message.Should().Contain("invalid model 'foo'");
    }

    [Fact]
    public async Task GenerateImageAsync_Http500WithBody_ReturnsMessageContainingStatusAndBody()
    {
        _nextResponse = () => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.InternalServerError,
            Content = new StringContent("upstream queue overflow")
        };

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat"
        };

        var result = await _service.GenerateImageAsync(parameters);

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("HTTP 500");
        result.Message.Should().Contain("upstream queue overflow");
    }

    [Fact]
    public async Task GenerateImageAsync_HttpErrorBodyContainingToken_RedactsTokenInReturnedMessage()
    {
        // Defense-in-depth: token lives on the Authorization header today, not the URL/body,
        // but the SUT redacts anyway in case a future change leaks it. Simulate a leak by
        // embedding the literal token in the error body.
        const string token = "tok-secret-XYZ";
        _nextResponse = () => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Forbidden,
            Content = new StringContent($"forbidden: bad token {token} not authorized")
        };

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            PollinationsApiToken = token
        };

        var result = await _service.GenerateImageAsync(parameters);

        result.Message.Should().Contain("[REDACTED]");
        result.Message.Should().NotContain(token);
    }

    [Fact]
    public async Task GenerateImageAsync_UndersizedSuccessResponse_ReturnsUndersizedMessage()
    {
        _nextResponse = () => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new ByteArrayContent(new byte[500])
        };

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat"
        };

        var result = await _service.GenerateImageAsync(parameters);

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("undersized");
        result.Message.Should().Contain("500");
    }

    [Fact]
    public async Task GenerateImageAsync_HandlerThrowsOperationCanceled_ReturnsCanceledMessage()
    {
        _nextResponse = () => throw new OperationCanceledException();

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat"
        };

        var result = await _service.GenerateImageAsync(parameters);

        result.ImageData.Should().BeNull();
        result.Message.Should().Be("Image generation was canceled.");
    }

    [Fact]
    public async Task GenerateImageAsync_SuccessResponse_ReturnsBytesAndSuccessMessage()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat"
        };

        var result = await _service.GenerateImageAsync(parameters);

        result.ImageData.Should().NotBeNull();
        result.ImageData!.Length.Should().Be(2048);
        result.Message.Should().StartWith("Image generated successfully");
        result.Message.Should().Contain(parameters.Model);
    }

    private long ExtractSeedFromCapturedUrl()
    {
        var seed = ExtractQueryParam("seed");
        seed.Should().NotBeNull("captured Pollinations URL must contain a seed= query param");
        return long.Parse(seed!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private string? ExtractQueryParam(string key)
    {
        _requests.Should().ContainSingle();
        var uri = _requests[0].RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        return query[key];
    }
}
