using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.External.Mutation;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

/// <summary>
/// Which provider an LLM target actually reaches, proven on the wire.
///
/// The services' own tests inject a fake completion, so they never run the production router that picks
/// a transport per provider: a service that re-derived the model from a table, or that sent an OpenRouter
/// call to Anthropic with an Anthropic key, passed them all. This file drives the real HTTP completion
/// against a recording handler — three providers, three endpoints, three auth headers, one recorded
/// request each — which is what makes "OpenRouter as a text provider" a shipped fact rather than an
/// assumption about the runtime.
/// </summary>
public sealed class LlmTransportRoutingTests
{
    private const string CaptionJson =
        """{"high_level_description":"A red fox stepping through fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","desc":"a russet-red fox mid-step, breath fogging"}]}}""";

    private const string AnthropicKey = "sk-ant-test";
    private const string OpenRouterKey = "sk-or-test";

    private static readonly V4JsonPrompt Base = V4JsonPromptSerializer.Deserialize(CaptionJson);

    [Theory]
    [InlineData(ModelProvider.Local, "127.0.0.1", "/api/chat")]
    [InlineData(ModelProvider.Anthropic, "api.anthropic.com", "/v1/messages")]
    [InlineData(ModelProvider.OpenRouter, "openrouter.ai", "/api/v1/chat/completions")]
    public async Task ProductionRouter_CallsTheTargetsOwnEndpoint(
        ModelProvider provider, string expectedHost, string expectedPath)
    {
        var http = new RecordingHandler(provider);
        var sut = CreateSut(http);

        var result = await sut.MutateAsync(Base, "make it winter", 0, Target(provider));

        result.Success.Should().BeTrue(http.LastBody ?? "<no request was made>");
        http.Requests.Should().HaveCount(1, "one mutation turn is one completion call");
        http.LastRequest!.RequestUri!.Host.Should().Be(expectedHost);
        http.LastRequest!.RequestUri!.AbsolutePath.Should().Be(expectedPath);
    }

    [Theory]
    [InlineData(ModelProvider.Local, "my-local-tag")]
    [InlineData(ModelProvider.Anthropic, "claude-haiku-5")]
    [InlineData(ModelProvider.OpenRouter, "some-lab/some-model")]
    public async Task ProductionRouter_CallsTheModelTheTargetNamed(
        ModelProvider provider, string modelId)
    {
        var http = new RecordingHandler(provider);
        var sut = CreateSut(http);

        var result = await sut.MutateAsync(
            Base, "make it winter", 0,
            new LlmTarget(provider, modelId, "http://127.0.0.1:11434"));

        result.Success.Should().BeTrue(http.LastBody ?? "<no request was made>");
        Json(http).GetProperty("model").GetString().Should().Be(modelId,
            "the model picked on the page is the model the provider is asked for — nothing re-derives it");
    }

    [Fact]
    public async Task ProductionRouter_OpenRouterTarget_IsAuthenticatedWithTheOpenRouterKey()
    {
        var http = new RecordingHandler(ModelProvider.OpenRouter);
        var sut = CreateSut(http);

        var result = await sut.MutateAsync(Base, "x", 0, Target(ModelProvider.OpenRouter));

        result.Success.Should().BeTrue(http.LastBody ?? "<no request was made>");
        http.LastRequest!.Headers.Authorization!.ToString().Should().Be($"Bearer {OpenRouterKey}",
            "an Anthropic key is not an OpenRouter key, and this is the header a 401 is read from");
        http.SentHeaders.Should().NotContain(h => h.Value == AnthropicKey,
            "the Anthropic key must not belong in a request to a different provider");
    }

    [Fact]
    public async Task ProductionRouter_AnthropicTarget_IsAuthenticatedWithTheAnthropicKey()
    {
        var http = new RecordingHandler(ModelProvider.Anthropic);
        var sut = CreateSut(http);

        var result = await sut.MutateAsync(Base, "x", 0, Target(ModelProvider.Anthropic));

        result.Error.Should().BeNull("the request must complete against the canned reply");
        http.SentHeaders.Should().Contain(h => h.Name == "x-api-key" && h.Value == AnthropicKey);
    }

    [Fact]
    public async Task ProductionRouter_LocalTarget_SendsNoKeyAtAll()
    {
        var http = new RecordingHandler(ModelProvider.Local);
        var sut = CreateSut(http);

        var result = await sut.MutateAsync(Base, "x", 0, Target(ModelProvider.Local));

        result.Success.Should().BeTrue(http.LastBody ?? "<no request was made>");
        http.SentHeaders.Should().NotContain(h => h.Name == "Authorization" || h.Name == "x-api-key",
            "the LAN Ollama server takes no auth, and sending a provider key to it would leak one");
    }

    // ---- Helpers ------------------------------------------------------------------------

    private static CaptionMutationLlmService CreateSut(RecordingHandler http) =>
        new(
            KeyStore<IAnthropicTokenStore>(AnthropicKey),
            KeyStore<IOpenRouterTokenStore>(OpenRouterKey),
            NullLogger<CaptionMutationLlmService>.Instance,
            new StubHttpClientFactory(http));

    private static T KeyStore<T>(string key) where T : class, ITokenStore
    {
        var mock = new Mock<T>();
        mock.Setup(s => s.LoadAsync()).ReturnsAsync(key);
        return mock.Object;
    }

    private static LlmTarget Target(ModelProvider provider) =>
        new(provider, "picked-model", "http://127.0.0.1:11434");

    private static JsonElement Json(RecordingHandler http) => JsonDocument.Parse(http.LastBody!).RootElement;

    /// <summary>Records every request and answers in each provider's own response shape, so a request
    /// that reached the wrong endpoint fails as "no message content" rather than passing.</summary>
    private sealed class RecordingHandler(ModelProvider provider) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<(string Name, string Value)> SentHeaders { get; } = [];
        public HttpRequestMessage? LastRequest => Requests.Count > 0 ? Requests[^1] : null;
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            foreach (var h in request.Headers)
                foreach (var v in h.Value)
                    SentHeaders.Add((h.Key, v));
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var reply = JsonSerializer.Serialize(CaptionJson);   // already a quoted JSON string
            var body = provider switch
            {
                ModelProvider.Local => "{\"message\":{\"content\":" + reply + "}}",
                ModelProvider.OpenRouter => "{\"choices\":[{\"message\":{\"content\":" + reply + "}}]}",
                _ => "{\"content\":[{\"type\":\"text\",\"text\":" + reply + "}]}",
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
