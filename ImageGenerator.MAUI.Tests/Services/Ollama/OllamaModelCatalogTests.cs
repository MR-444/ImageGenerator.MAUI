using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Ollama;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ImageGenerator.MAUI.Tests.Services.Ollama;

public sealed class OllamaModelCatalogTests
{
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly List<string> _bodies = [];

    private OllamaModelCatalog BuildCatalog(Func<HttpResponseMessage> respond)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                _requests.Add(req);
                _bodies.Add(req.Content?.ReadAsStringAsync().Result ?? string.Empty);
            })
            .ReturnsAsync((HttpRequestMessage _, CancellationToken _) => respond());

        var client = new HttpClient(handler.Object);
        return new OllamaModelCatalog(new StubHttpClientFactory(client), NullLogger<OllamaModelCatalog>.Instance);
    }

    [Fact]
    public async Task UnloadAsync_PostsKeepAliveZeroForTheModel()
    {
        var catalog = BuildCatalog(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK));

        await catalog.UnloadAsync("http://fireengine:11434", "qwen3.5:27b");

        _requests.Should().ContainSingle();
        _requests[0].Method.Should().Be(HttpMethod.Post);
        _requests[0].RequestUri!.AbsoluteUri.Should().Be("http://fireengine:11434/api/generate");
        _bodies[0].Should().Contain("\"model\":\"qwen3.5:27b\"").And.Contain("\"keep_alive\":0");
    }

    [Fact]
    public async Task UnloadAsync_EmptyBaseUrlOrModel_MakesNoRequest()
    {
        var catalog = BuildCatalog(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK));

        await catalog.UnloadAsync("", "qwen3.5:27b");
        await catalog.UnloadAsync("http://fireengine:11434", "");

        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnloadAsync_ServerThrows_DoesNotThrow()
    {
        var catalog = BuildCatalog(() => throw new HttpRequestException("server down"));

        var act = async () => await catalog.UnloadAsync("http://fireengine:11434", "qwen3.5:27b");

        await act.Should().NotThrowAsync("the unload is a best-effort GPU-free, not part of the result");
    }
}
