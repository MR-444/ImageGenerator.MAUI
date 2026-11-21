using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Services.Anthropic;

/// <summary>
/// The catalogue is the app's only claim about which Claude ids exist, so these tests aim at the ways
/// that claim goes silently wrong: asking for one page of twenty and calling it the list, and offering
/// a model the endpoint will not run.
/// </summary>
public sealed class AnthropicModelCatalogTests
{
    private const string Key = "sk-ant-test";

    private readonly List<HttpRequestMessage> _requests = [];

    private AnthropicModelCatalog Build(params (int Status, string Body)[] pages)
    {
        var handler = new QueueHandler(new Queue<(int, string)>(pages), _requests);
        var client = new HttpClient(handler);
        return new AnthropicModelCatalog(new StubHttpClientFactory(client), NullLogger<AnthropicModelCatalog>.Instance);
    }

    private static string Page(string modelsJson, string? lastId, bool hasMore) =>
        $$"""{"data":[{{modelsJson}}],"first_id":"first","last_id":{{(lastId is null ? "null" : $"\"{lastId}\"")}},"has_more":{{(hasMore ? "true" : "false")}}}""";

    private const string Opus =
        """{"type":"model","id":"claude-opus-5","display_name":"Claude Opus 5","lifecycle":"active","capabilities":{"image_input":{"supported":true}}}""";
    private const string Sonnet =
        """{"type":"model","id":"claude-sonnet-4-5","display_name":"Claude Sonnet 4.5","lifecycle":"active","capabilities":{"image_input":{"supported":true}}}""";

    [Fact]
    public async Task AsksForMoreThanTheTwentyRowDefaultPageSize()
    {
        // "limit … Defaults to 20." A request that does not say otherwise gets the twenty newest
        // models, a valid response, and a picker that quietly omits everything older.
        var catalog = Build((200, Page(Opus, null, false)));

        await catalog.ListTextModelsAsync(Key);

        _requests.Single().RequestUri!.Query.Should().Contain($"limit={AnthropicModelCatalog.PageSize}");
        _requests.Single().RequestUri!.AbsoluteUri.Should().StartWith("https://api.anthropic.com/v1/models");
    }

    [Fact]
    public async Task SendsTheSameKeyAndVersionHeadersAMessagesCallSends()
    {
        var catalog = Build((200, Page(Opus, null, false)));

        await catalog.ListTextModelsAsync(Key);

        var request = _requests.Single();
        request.Headers.TryGetValues("x-api-key", out var sentKey).Should().BeTrue();
        sentKey!.Single().Should().Be(Key);
        request.Headers.TryGetValues("anthropic-version", out var sentVersion).Should().BeTrue();
        sentVersion!.Single().Should().Be(AnthropicMessagesTransport.AnthropicVersion,
            "one app makes one claim about which API version it speaks");
    }

    [Fact]
    public async Task KeepsPagingUntilTheServerStopsOfferingMore()
    {
        var catalog = Build(
            (200, Page(Opus, "claude-opus-5", true)),
            (200, Page(Sonnet, "claude-sonnet-4-5", false)));

        var models = await catalog.ListTextModelsAsync(Key);

        // Order is the endpoint's, newest first, kept rather than re-sorted.
        models.Select(m => m.Id).Should().Equal("claude-opus-5", "claude-sonnet-4-5");
        _requests.Should().HaveCount(2);
        _requests[1].RequestUri!.Query.Should().Contain("after_id=claude-opus-5");
    }

    [Fact]
    public async Task StopsWhenTheCursorRefusesToAdvance()
    {
        // A server that ignores after_id would otherwise be asked for page after page of the same
        // models, forever, with a spinner in the way.
        var catalog = Build(
            (200, Page(Opus, "same", true)),
            (200, Page(Sonnet, "same", true)),
            (200, Page(Sonnet, "same", true)));

        var models = await catalog.ListTextModelsAsync(Key);

        models.Should().HaveCount(2);
        _requests.Should().HaveCount(2, "an unchanged cursor is a stop condition, not a reason to retry");
    }

    [Fact]
    public async Task DoesNotOfferRetiredModels()
    {
        // They are absent when lifecycle is omitted; if one arrives anyway, it still cannot run.
        var retired = """{"id":"claude-opus-4","display_name":"Claude Opus 4","lifecycle":"retired"}""";
        var catalog = Build((200, Page($"{Opus},{retired}", null, false)));

        var models = await catalog.ListTextModelsAsync(Key);

        models.Select(m => m.Id).Should().BeEquivalentTo(["claude-opus-5"]);
    }

    [Fact]
    public async Task OffersDeprecatedModels_ButSaysSoOnTheLabel()
    {
        // Deprecated means "callable by the keys that already have it, closed to new adopters". Which
        // keys those are is not ours to guess, so the row stays and carries the word.
        var deprecated = """{"id":"claude-2","display_name":"Claude 2","lifecycle":"deprecated"}""";
        var catalog = Build((200, Page(deprecated, null, false)));

        var model = (await catalog.ListTextModelsAsync(Key)).Single();

        model.IsDeprecated.Should().BeTrue();
        model.Display.Should().Contain("deprecated");
    }

    [Fact]
    public async Task ToleratesAResponseWithoutLifecycleOrCapabilities()
    {
        // Which optional fields arrive depends on the pinned anthropic-version; a thin row is still an
        // id that can be called, and emptying the picker over a missing field loses a working model.
        var catalog = Build((200, Page("""{"id":"claude-3-haiku"}""", null, false)));

        var model = (await catalog.ListTextModelsAsync(Key)).Single();

        model.Lifecycle.Should().Be(AnthropicModelInfo.Active);
        model.DisplayName.Should().Be("claude-3-haiku", "no display_name is not a missing name");
        model.AcceptsImages.Should().BeFalse("an unread capability is not a claim that it can see");
        model.IsDeprecated.Should().BeFalse();
    }

    [Fact]
    public async Task ToleratesAPageWithNoDataArray()
    {
        var catalog = Build((200, """{"has_more":false}"""));

        var models = await catalog.ListTextModelsAsync(Key);

        models.Should().BeEmpty();
        _requests.Should().HaveCount(1, "no data is not a reason to ask again");
    }

    [Fact]
    public async Task RefusesToAskTheNetwork_WhenNoKeyIsSet()
    {
        var catalog = Build();

        var act = () => catalog.ListTextModelsAsync("  ");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Anthropic API key", "the caller shows this sentence in the row's status line");
        _requests.Should().BeEmpty("a 401 would only translate into a vaguer version of the same message");
    }

    [Fact]
    public async Task ReportsTheStatusCodeWhenListingFails()
    {
        var catalog = Build((429, "slow down"));

        var act = () => catalog.ListTextModelsAsync(Key);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("429", "the caller shows this sentence in the row's status line");
    }

    [Fact]
    public void DisplayAlwaysCarriesTheId()
    {
        // Two models can share a display name; a picker that hides the id cannot be debugged by reading it.
        new AnthropicModelInfo("claude-x", "Claude X", AnthropicModelInfo.Active, false)
            .Display.Should().Be("Claude X (claude-x)");
    }

    [Fact]
    public void CatalogIsReachableThroughItsInterface()
    {
        typeof(IAnthropicModelCatalog).IsAssignableFrom(typeof(AnthropicModelCatalog))
            .Should().BeTrue("callers depend on the interface, never the class");
    }

    private sealed class QueueHandler(Queue<(int Status, string Body)> pages, List<HttpRequestMessage> requests)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            requests.Add(request);
            var (status, body) = pages.Count > 0
                ? pages.Dequeue()
                : (200, """{"data":[],"has_more":false}""");
            return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status)
            {
                Content = new StringContent(body)
            });
        }
    }
}
