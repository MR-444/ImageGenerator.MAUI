using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Vision;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Services.OpenRouter;

/// <summary>
/// Two lists, one endpoint, one real difference: a model that can see is not the same set as a model
/// that can write. These tests keep the two filters from collapsing into each other.
/// </summary>
public sealed class OpenRouterModelCatalogTests
{
    private readonly List<string> _clientNames = [];

    private OpenRouterModelCatalog Build(string body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        // A fresh HttpClient per CreateClient call, as a real IHttpClientFactory hands out. The catalog
        // disposes what it borrows (that is the factory contract), so a shared client would die after
        // the first listing and the test would be measuring its own harness.
        var handler = new JsonHandler(status, body);
        return new OpenRouterModelCatalog(
            new NamingFactory(handler, _clientNames),
            NullLogger<OpenRouterModelCatalog>.Instance);
    }

    private static string Row(string id, string[] input, string[] output, string price = "0.000005") =>
        "{\"id\":\"" + id + "\",\"name\":\"" + id + "\","
        + "\"architecture\":{\"input_modalities\":[" + Join(input) + "],\"output_modalities\":[" + Join(output) + "]},"
        + "\"pricing\":{\"prompt\":\"" + price + "\",\"completion\":\"" + price + "\"}}";

    private static string Join(string[] items) => string.Join(",", items.Select(i => "\"" + i + "\""));

    private static string Catalog(params string[] rows) => "{\"data\":[" + string.Join(",", rows) + "]}";

    [Fact]
    public async Task ChatListIncludesTheTextWriterTheVisionListLeavesOut()
    {
        // The reason the second method exists: a prose writer that takes no pixels is a perfectly good
        // writer, and filtering it out because it cannot see would leave a role with nothing to pick.
        var catalog = Build(Catalog(
            Row("acme/writer", ["text"], ["text"]),
            Row("acme/seer", ["text", "image"], ["text"])));

        var chat = await catalog.ListChatModelsAsync(freeOnly: false);

        chat.Select(m => m.Id).Should().Contain("acme/writer");
        chat.Should().HaveCount(2, "both can answer in text; that is the only requirement");
    }

    [Fact]
    public async Task ChatListLeavesOutAnythingThatDoesNotAnswerInText()
    {
        var catalog = Build(Catalog(
            Row("acme/writer", ["text"], ["text"]),
            Row("acme/embeddings", ["text"], ["embedding"]),
            Row("acme/painter", ["text"], ["image"])));

        var chat = await catalog.ListChatModelsAsync(freeOnly: false);

        chat.Select(m => m.Id).Should().BeEquivalentTo(["acme/writer"]);
    }

    [Fact]
    public async Task VisionListStillRequiresImageInput()
    {
        // The extraction that gave both methods one fetch must not have softened this.
        var catalog = Build(Catalog(
            Row("acme/writer", ["text"], ["text"]),
            Row("acme/seer", ["text", "image"], ["text"])));

        var vision = await catalog.ListVisionModelsAsync(freeOnly: false);

        vision.Select(m => m.Id).Should().BeEquivalentTo(["acme/seer"]);
    }

    [Fact]
    public async Task FreeOnlyAppliesToBothLists_AndPutsFreeRowsFirst()
    {
        var catalog = Build(Catalog(
            Row("acme/paid", ["text"], ["text"], price: "0.005"),
            Row("acme/free", ["text"], ["text"], price: "0")));

        var all = await catalog.ListChatModelsAsync(freeOnly: false);
        var free = await catalog.ListChatModelsAsync(freeOnly: true);

        all.Select(m => m.Id).Should().Equal("acme/free", "acme/paid");
        free.Select(m => m.Id).Should().BeEquivalentTo(["acme/free"]);
    }

    [Fact]
    public async Task BothListsReadTheSameCatalogClient()
    {
        var catalog = Build(Catalog(Row("acme/writer", ["text"], ["text"])));

        await catalog.ListChatModelsAsync(freeOnly: false);
        await catalog.ListVisionModelsAsync(freeOnly: false);

        _clientNames.Should().OnlyContain(n => n == VisionObservationService.OpenRouterHttpClientName);
    }

    [Fact]
    public async Task AFailedListingSaysTheSameThingWhicheverListWasAskedFor()
    {
        // The caller shows this sentence in the row's status line; two wordings for one failure is two
        // chances for one of them to be the unhelpful one.
        var catalog = Build("server exploded", System.Net.HttpStatusCode.InternalServerError);

        var chat = await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.ListChatModelsAsync(freeOnly: false));
        var vision = await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.ListVisionModelsAsync(freeOnly: false));

        chat.Message.Should().Be(vision.Message).And.Contain("500");
    }

    private sealed class JsonHandler(System.Net.HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private sealed class NamingFactory(HttpMessageHandler handler, List<string> names) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            names.Add(name);
            return new HttpClient(handler);
        }
    }
}
