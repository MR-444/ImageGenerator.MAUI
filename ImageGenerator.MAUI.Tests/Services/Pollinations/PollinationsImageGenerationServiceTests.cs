using System.Net;
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
    private readonly List<HttpRequestMessage> _requests = [];
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
    public async Task GenerateImageAsync_GptImageModel_SendsQualityQueryParam()
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.GptImage2,
            Prompt = "a cat",
            GptQuality = "high"
        };

        await _service.GenerateImageAsync(parameters);

        ExtractQueryParam("quality").Should().Be("high");
    }

    [Theory]
    [InlineData("auto")]   // the Replicate-hosted default, which Pollinations rejects outright
    [InlineData("")]
    [InlineData("bogus")]
    public async Task GenerateImageAsync_GptImageModel_OffMenuQuality_FallsBackToMedium(string quality)
    {
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.GptImage,
            Prompt = "a cat",
            GptQuality = quality
        };

        await _service.GenerateImageAsync(parameters);

        ExtractQueryParam("quality").Should().Be("medium");
    }

    [Fact]
    public async Task GenerateImageAsync_NonGptImageModel_OmitsQualityQueryParam()
    {
        // Only gptimage/gptimage-large/gpt-image-2 accept `quality`; sending it elsewhere 400s.
        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat",
            GptQuality = "high"
        };

        await _service.GenerateImageAsync(parameters);

        ExtractQueryParam("quality").Should().BeNull();
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
        auth.Scheme.Should().Be("Bearer");
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
    public async Task GenerateImageAsync_Http401_ReturnsApiKeyGuidanceInsteadOfRawEnvelope()
    {
        // Pollinations made auth mandatory — anonymous generation now 401s with a JSON envelope
        // that reads badly in a job-row label. The user needs to be told to add a key.
        _nextResponse = () => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent(
                """{"success":false,"error":{"message":"Authentication required.","code":"UNAUTHORIZED"},"status":401}""")
        };

        var parameters = new ImageGenerationParameters
        {
            Model = ModelConstants.Pollinations.Flux,
            Prompt = "a cat"
        };

        var result = await _service.GenerateImageAsync(parameters);

        result.ImageData.Should().BeNull();
        result.Message.Should().Contain("API key");
        result.Message.Should().Contain("enter.pollinations.ai");
        result.Message.Should().NotContain("UNAUTHORIZED");
        // 401 covers "missing" and "invalid" alike; the upstream sentence is what tells them apart, so
        // translating must not throw it away.
        result.Message.Should().Contain("Authentication required");
    }

    [Fact]
    public async Task GenerateImageAsync_Http402InsufficientBalance_NamesTheAccountAndNotTheModel()
    {
        var result = await FailureAsync(HttpStatusCode.PaymentRequired, ModelConstants.Pollinations.QwenImage,
            """{"status":402,"success":false,"error":{"code":"INSUFFICIENT_BALANCE","message":"Insufficient balance"}}""");

        result.Message.Should().Contain("out of pollen").And.Contain("enter.pollinations.ai");
        result.Message.Should().Contain("Insufficient balance");
    }

    [Fact]
    public async Task GenerateImageAsync_Http402KeyBudgetExhausted_BlamesTheKeyAndNotTheModel()
    {
        var result = await FailureAsync(HttpStatusCode.PaymentRequired, ModelConstants.Pollinations.QwenImage,
            """{"status":402,"success":false,"error":{"code":"KEY_BUDGET_EXHAUSTED","message":"Key budget exhausted"}}""");

        result.Message.Should().Contain("budget").And.Contain("enter.pollinations.ai/keys");
        result.Message.Should().Contain("Key budget exhausted");
    }

    /// <summary>
    /// The reported bug: a model the catalog does not mark paid, refused with a 402, answered by a message
    /// claiming it was a paid model. The documented code says the account cannot pay for the request, so
    /// the message has to say that and leave the model alone.
    /// </summary>
    [Fact]
    public async Task GenerateImageAsync_Http402QuestPollenOnly_OnAModelNotMarkedPaid_DoesNotCallItPaid()
    {
        var result = await FailureAsync(HttpStatusCode.PaymentRequired, ModelConstants.Pollinations.Flux,
            """{"status":402,"success":false,"error":{"code":"QUEST_POLLEN_ONLY","message":"Quest pollen only"}}""");

        result.Message.Should().Contain("Quest");
        result.Message.Should().Contain("not proof that the model is marked paid");
        result.Message.Should().Contain("Quest pollen only");
    }

    /// <summary>
    /// A code this build has not been told about is not evidence about anything, so nothing is concluded:
    /// the status and whatever upstream wrote are shown, exactly as for every other status.
    /// </summary>
    [Fact]
    public async Task GenerateImageAsync_Http402WithAnUnknownCode_QuotesUpstreamInsteadOfGuessing()
    {
        var result = await FailureAsync(HttpStatusCode.PaymentRequired, ModelConstants.Pollinations.Flux,
            """{"status":402,"success":false,"error":{"code":"PAYMENT_REQUIRED","message":"Insufficient balance"}}""");

        result.Message.Should().Contain("HTTP 402").And.Contain("Insufficient balance");
        result.Message.Should().NotContain("top up");
    }

    [Fact]
    public async Task GenerateImageAsync_Http402FromAGatewayPage_QuotesTheBody()
    {
        var result = await FailureAsync(HttpStatusCode.PaymentRequired, ModelConstants.Pollinations.Flux,
            "<html><body>Payment Required</body></html>");

        result.Message.Should().Contain("HTTP 402").And.Contain("Payment Required");
    }

    /// <summary>
    /// The translator gets the whole body: cutting it can halve the error envelope, and half an envelope
    /// makes a refusal look unexplained. Truncation belongs to the job-row label, not to the parse.
    /// </summary>
    [Fact]
    public async Task GenerateImageAsync_Http402WithALongBody_StillReadsTheCode()
    {
        var padding = new string('x', 900);
        var body = "{\"status\":402,\"success\":false,\"error\":{\"code\":\"KEY_BUDGET_EXHAUSTED\"," +
            "\"message\":\"budget gone " + padding + "\"}}";
        var result = await FailureAsync(HttpStatusCode.PaymentRequired, ModelConstants.Pollinations.QwenImage,
            body);

        // Asserted on the translated sentence: the raw body says "budget" too, so quoting it back would
        // look like a pass while the code went unread.
        result.Message.Should().Contain("has spent the budget set on it").And.Contain("budget gone");
    }

    private async Task<GeneratedImage> FailureAsync(HttpStatusCode status, string model, string body)
    {
        _nextResponse = () => new HttpResponseMessage
        {
            StatusCode = status,
            Content = new StringContent(body)
        };

        return await _service.GenerateImageAsync(new ImageGenerationParameters
        {
            Model = model,
            Prompt = "a cat"
        });
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
        return long.Parse(seed, System.Globalization.CultureInfo.InvariantCulture);
    }

    private string? ExtractQueryParam(string key)
    {
        _requests.Should().ContainSingle();
        var uri = _requests[0].RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        return query[key];
    }
}
