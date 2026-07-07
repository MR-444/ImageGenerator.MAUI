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
    public async Task ListModelInfosAsync_UsesCatalogClientAndParsesCapabilities()
    {
        var factory = new CapturingFactory(new HttpClient(new JsonHandler(
            """{"models":[{"name":"qwen3-vl","capabilities":["vision","completion"]},{"name":"nomic","capabilities":["embedding"]}]}""")));
        var catalog = new OllamaModelCatalog(factory, NullLogger<OllamaModelCatalog>.Instance);

        var models = await catalog.ListModelInfosAsync("http://fireengine:11434");

        // Every client (the /api/tags list + any /api/show vision probe) comes from the short-timeout
        // catalog client. "nomic" lacks vision in /api/tags, so it triggers one /api/show probe; the
        // stub body has no top-level capabilities, so no vision is grafted on.
        factory.Names.Should().NotBeEmpty()
            .And.OnlyContain(n => n == OllamaModelCatalog.HttpClientName);
        models.Should().HaveCount(2);
        models[0].Name.Should().Be("nomic", "model names are sorted for picker stability");
        models[0].SupportsVision.Should().BeFalse();
        models[1].Name.Should().Be("qwen3-vl");
        models[1].SupportsVision.Should().BeTrue();
        models[1].SupportsCompletion.Should().BeTrue();
    }

    [Fact]
    public async Task ListModelInfosAsync_ConfirmsVisionViaApiShow_WhenTagsUnderReport()
    {
        // Ollama's /api/tags under-reports the vision capability for gemma3/gemma4; /api/show reports it
        // authoritatively. The catalog must consult /api/show for any model /api/tags didn't flag.
        var factory = new CapturingFactory(new HttpClient(new RoutingHandler()));
        var catalog = new OllamaModelCatalog(factory, NullLogger<OllamaModelCatalog>.Instance);

        var models = await catalog.ListModelInfosAsync("http://fireengine:11434");

        models.Single(m => m.Name == "gemma4:31b").SupportsVision
            .Should().BeTrue("/api/show reports vision even though /api/tags omitted it");
        models.Single(m => m.Name == "deepseek-r1").SupportsVision
            .Should().BeFalse("a genuinely text-only model stays non-vision after the probe");
        models.Single(m => m.Name == "qwen3-vl").SupportsVision
            .Should().BeTrue("already flagged vision by /api/tags");
    }

    [Fact]
    public async Task UnloadAsync_StillUsesLongGenerationClient()
    {
        var factory = new CapturingFactory(new HttpClient(new JsonHandler("{}")));
        var catalog = new OllamaModelCatalog(factory, NullLogger<OllamaModelCatalog>.Instance);

        await catalog.UnloadAsync("http://fireengine:11434", "qwen3-vl");

        factory.Names.Should().ContainSingle().Which.Should().Be(OllamaChatTransport.HttpClientName);
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

    private sealed class CapturingFactory(HttpClient client) : IHttpClientFactory
    {
        public List<string> Names { get; } = [];

        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            return client;
        }
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    // Routes by endpoint: /api/tags lists a gemma tag missing vision, a text-only model, and a
    // vision-flagged qwen; /api/show reports vision only for gemma (keyed off the posted model name).
    private sealed class RoutingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string json;
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/tags"))
            {
                json = """
                    {"models":[
                      {"name":"gemma4:31b","capabilities":["completion","tools"]},
                      {"name":"deepseek-r1","capabilities":["completion"]},
                      {"name":"qwen3-vl","capabilities":["vision","completion"]}]}
                    """;
            }
            else
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                json = body.Contains("gemma4:31b")
                    ? """{"capabilities":["completion","vision","tools"]}"""
                    : """{"capabilities":["completion"]}""";
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
