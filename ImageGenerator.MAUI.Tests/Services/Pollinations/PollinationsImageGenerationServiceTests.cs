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
    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Strict);
    private readonly List<HttpRequestMessage> _requests = new();
    private readonly PollinationsImageGenerationService _service;

    public PollinationsImageGenerationServiceTests()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _requests.Add(req))
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ByteArrayContent(new byte[2048])
            });

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

    private long ExtractSeedFromCapturedUrl()
    {
        _requests.Should().ContainSingle();
        var uri = _requests[0].RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);
        var seed = query["seed"];
        seed.Should().NotBeNull("captured Pollinations URL must contain a seed= query param");
        return long.Parse(seed!, System.Globalization.CultureInfo.InvariantCulture);
    }
}
