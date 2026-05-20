using System.Net;
using System.Text;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.External.Pollinations;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ImageGenerator.MAUI.Tests.Services.Pollinations;

public class PollinationsCatalogServiceTests
{
    // Loose mode is required because the SUT uses `using var httpClient = ...`, which calls
    // HttpClient.Dispose → handler.Dispose at end of scope. Strict would throw on Dispose
    // and route every test through the swallow-and-return-empty catch block.
    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Loose);
    private readonly PollinationsCatalogService _service;

    // Per-test response factory. Default returns an empty JSON array so a test that forgets
    // to set this gets a clean empty result (no false greens off some prior payload).
    private Func<HttpResponseMessage> _nextResponse =
        () => JsonResponse("[]");

    public PollinationsCatalogServiceTests()
    {
        _handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => _nextResponse());

        _service = new PollinationsCatalogService(
            new StubHttpClientFactory(new HttpClient(_handler.Object)),
            NullLogger<PollinationsCatalogService>.Instance);
    }

    [Fact]
    public async Task FetchAsync_ReturnsImageNonPaidEntries()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"flux","description":"Flux Schnell - Fast generator","output_modalities":["image"],"paid_only":false}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Should().HaveCount(1);
        result[0].Display.Should().Be("Flux Schnell (Pollinations)");
        result[0].Value.Should().Be("pollinations/flux");
        result[0].Provider.Should().Be(ProviderConstants.Pollinations);
    }

    [Fact]
    public async Task FetchAsync_FiltersOutEntriesMissingImageModality()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"img-model","output_modalities":["image"]},
              {"name":"text-model","output_modalities":["text"]},
              {"name":"null-modality"}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Select(r => r.Value).Should().BeEquivalentTo(["pollinations/img-model"]);
    }

    [Fact]
    public async Task FetchAsync_FiltersOutPaidOnlyTrueEntries()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"free","output_modalities":["image"],"paid_only":false},
              {"name":"paid","output_modalities":["image"],"paid_only":true}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Select(r => r.Value).Should().BeEquivalentTo(["pollinations/free"]);
    }

    [Fact]
    public async Task FetchAsync_IncludesEntriesWithNullPaidOnly()
    {
        // paid_only key omitted entirely — `e.PaidOnly != true` allows null through.
        _nextResponse = () => JsonResponse("""
            [
              {"name":"unknown-payment","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Should().HaveCount(1);
        result[0].Value.Should().Be("pollinations/unknown-payment");
    }

    [Fact]
    public async Task FetchAsync_FiltersOutEntriesWithBlankOrNullName()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"good","output_modalities":["image"]},
              {"name":"","output_modalities":["image"]},
              {"name":"   ","output_modalities":["image"]},
              {"name":null,"output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Select(r => r.Value).Should().BeEquivalentTo(["pollinations/good"]);
    }

    [Fact]
    public async Task FetchAsync_DisplayName_UsesDescriptionPrefixBeforeDash()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"flux","description":"Flux Schnell - Fast generator","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result[0].Display.Should().Be("Flux Schnell (Pollinations)");
    }

    [Fact]
    public async Task FetchAsync_DisplayName_UsesFullDescriptionWhenNoDashPresent()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"x","description":"Plain description","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result[0].Display.Should().Be("Plain description (Pollinations)");
    }

    [Fact]
    public async Task FetchAsync_DisplayName_FallsBackToTitleCasedSlug_WhenDescriptionMissing()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"flux-pro-ultra","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result[0].Display.Should().Be("Flux Pro Ultra (Pollinations)");
    }

    [Fact]
    public async Task FetchAsync_DisplayName_AlwaysAppendsPollinationsSuffix()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"a","description":"With dash - tail","output_modalities":["image"]},
              {"name":"b","description":"NoDash","output_modalities":["image"]},
              {"name":"slug-only","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Should().HaveCount(3);
        result.Should().OnlyContain(r => r.Display.EndsWith(" (Pollinations)"));
    }

    [Fact]
    public async Task FetchAsync_ValueIsPollinationsPrefixedName()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"gptimage","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result[0].Value.Should().Be(ModelConstants.Pollinations.PrefixSlash + "gptimage");
    }

    [Fact]
    public async Task FetchAsync_ReturnsEmpty_OnHttpError()
    {
        _nextResponse = () => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.InternalServerError,
            Content = new StringContent("oops")
        };

        var result = await _service.FetchAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ReturnsEmpty_OnInvalidJson()
    {
        _nextResponse = () => JsonResponse("this is not json");

        var result = await _service.FetchAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ReturnsEmpty_OnJsonNullPayload()
    {
        // `GetFromJsonAsync<List<...>>("null")` deserializes to null, exercising the
        // `entries is null` early-return path (not the catch block).
        _nextResponse = () => JsonResponse("null");

        var result = await _service.FetchAsync();

        result.Should().BeEmpty();
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new()
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
