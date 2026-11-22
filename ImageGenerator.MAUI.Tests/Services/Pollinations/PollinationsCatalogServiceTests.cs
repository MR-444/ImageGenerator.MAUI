using System.Net;
using System.Text;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
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
            NullLogger<PollinationsCatalogService>.Instance,
            ModelDescriptorRegistry.Default());
    }

    [Fact]
    public async Task FetchAsync_MapsEntryToModelOption()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"flux","title":"FLUX.1 Schnell","description":"Fast generator","output_modalities":["image"],"paid_only":false}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Should().HaveCount(1);
        result[0].Display.Should().Be("FLUX.1 Schnell (Pollinations)");
        result[0].Value.Should().Be("pollinations/flux");
        result[0].Provider.Should().Be(ProviderConstants.Pollinations);
    }

    /// <summary>
    /// The catalog moved to <c>publisher/model</c> names and kept the old slugs as aliases, which the docs
    /// say stay valid in requests. Answering with the id this build describes — rather than the name the
    /// feed leads with — is what keeps the descriptor's knobs, the user's saved selection and the app's own
    /// seed entry attached to one model, instead of opening a second, description-less row beside it.
    /// </summary>
    [Fact]
    public async Task FetchAsync_DescribedModelRenamedUpstream_KeepsTheDescribedId()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"black-forest-labs/flux.1-schnell","aliases":["flux"],"title":"FLUX.1 Schnell","output_modalities":["image"],"paid_only":false}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Should().ContainSingle().Which.Value.Should().Be(ModelConstants.Pollinations.Flux);
        // Display still comes from the feed: it is what carries the paid tag, and a seed label cannot.
        result[0].Display.Should().Be("FLUX.1 Schnell (Pollinations)");
    }

    [Fact]
    public async Task FetchAsync_ModelNobodyDescribed_KeepsTheCanonicalId()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"microsoft/mai-image-2.7","aliases":[],"title":"MAI Image 2.7","output_modalities":["image"],"paid_only":false}
            ]
            """);

        var result = await _service.FetchAsync();

        // A model added upstream after this build shipped is still reachable: the Pollinations fallback
        // descriptor derives the request name from the id, and the canonical id is what the feed calls it.
        result[0].Value.Should().Be("pollinations/microsoft/mai-image-2.7");
    }

    /// <summary>
    /// Alias matching can land two feed entries on one row. One of them is dropped, so the value the picker
    /// binds never repeats — and the row that survives is the one the feed listed first.
    /// </summary>
    [Fact]
    public async Task FetchAsync_TwoEntriesMapToTheSameId_YieldsOneRow()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"openai/gpt-image-1.5","aliases":["gptimage-large"],"title":"GPT Image 1.5","output_modalities":["image"],"paid_only":true},
              {"name":"gptimage-large","aliases":[],"title":"GPT Image Large","output_modalities":["image"],"paid_only":true}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Should().ContainSingle().Which.Value.Should().Be(ModelConstants.Pollinations.GptImageLarge);
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
    public async Task FetchAsync_KeepsPaidOnlyEntries_TaggedPaid()
    {
        // Every request needs an API key now, so paid_only is about pollen credits rather than
        // reachability — the models stay listed and carry the tag instead of being filtered out.
        _nextResponse = () => JsonResponse("""
            [
              {"name":"free","title":"Free Model","output_modalities":["image"],"paid_only":false},
              {"name":"paid","title":"Paid Model","output_modalities":["image"],"paid_only":true}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Select(r => r.Display).Should().BeEquivalentTo([
            "Free Model (Pollinations)",
            "Paid Model (Pollinations · paid)"
        ]);
    }

    [Fact]
    public async Task FetchAsync_TreatsMissingPaidOnlyAsFree()
    {
        // paid_only key omitted entirely — only an explicit `true` earns the tag.
        _nextResponse = () => JsonResponse("""
            [
              {"name":"unknown-payment","title":"Unknown","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Should().HaveCount(1);
        result[0].Value.Should().Be("pollinations/unknown-payment");
        result[0].Display.Should().Be("Unknown (Pollinations)");
    }

    [Fact]
    public async Task FetchAsync_FiltersOutCommunityAndAlphaEntries()
    {
        // Community models run on the owner's own backend, not Pollinations infrastructure, so
        // prompts would leave to a third party — excluded regardless of the paid_only flag.
        _nextResponse = () => JsonResponse("""
            [
              {"name":"first-party","title":"First Party","output_modalities":["image"]},
              {"name":"vendouple/lucid-origin","title":"Lucid Origin","output_modalities":["image"],"community":true,"alpha":true,"paid_only":false},
              {"name":"community-only","title":"Community Only","output_modalities":["image"],"community":true},
              {"name":"alpha-only","title":"Alpha Only","output_modalities":["image"],"alpha":true}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Select(r => r.Value).Should().BeEquivalentTo(["pollinations/first-party"]);
    }

    [Fact]
    public async Task FetchAsync_FiltersOutVideoModels_FromImageModelsFeed()
    {
        // /image/models carries image AND video models; only image output belongs in the picker.
        _nextResponse = () => JsonResponse("""
            [
              {"name":"flux","title":"FLUX.1 Schnell","output_modalities":["image"]},
              {"name":"veo","title":"Veo","output_modalities":["video"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Select(r => r.Value).Should().BeEquivalentTo(["pollinations/flux"]);
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
    public async Task FetchAsync_DisplayName_UsesTitleField()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"zimage","title":"Z-Image Turbo","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result[0].Display.Should().Be("Z-Image Turbo (Pollinations)");
    }

    [Fact]
    public async Task FetchAsync_DisplayName_NeverUsesDescription()
    {
        // The model name is in "title"; "description" is a marketing sentence and must not
        // surface as the name.
        _nextResponse = () => JsonResponse("""
            [
              {"name":"flux","title":"FLUX.1 Schnell","description":"Fast, high-quality images at a tiny cost","output_modalities":["image"]},
              {"name":"kontext","description":"Edits an existing image - swap, restyle, refine","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Select(r => r.Display).Should().BeEquivalentTo([
            "FLUX.1 Schnell (Pollinations)",
            "Kontext (Pollinations)"
        ]);
    }

    [Fact]
    public async Task FetchAsync_DisplayName_FallsBackToTitleCasedSlug_WhenTitleMissing()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"flux-pro-ultra","output_modalities":["image"]},
              {"name":"x","title":"   ","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result.Select(r => r.Display).Should().BeEquivalentTo([
            "Flux Pro Ultra (Pollinations)",
            "X (Pollinations)"
        ]);
    }

    [Fact]
    public async Task FetchAsync_DisplayName_SlugFallbackDropsVendorNamespace()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"vendouple/lucid-origin","output_modalities":["image"]}
            ]
            """);

        var result = await _service.FetchAsync();

        result[0].Display.Should().Be("Lucid Origin (Pollinations)");
    }

    [Fact]
    public async Task FetchAsync_DisplayName_AlwaysAppendsPollinationsSuffix()
    {
        _nextResponse = () => JsonResponse("""
            [
              {"name":"a","title":"Titled","output_modalities":["image"]},
              {"name":"b","description":"Description only","output_modalities":["image"]},
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
