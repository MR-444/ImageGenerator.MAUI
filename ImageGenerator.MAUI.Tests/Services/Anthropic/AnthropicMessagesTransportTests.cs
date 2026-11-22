using System.Net;
using System.Text;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.External.Mutation;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services.Anthropic;

/// <summary>
/// The Messages transport on the wire: which API version it claims, and what it does when the model
/// declines. A refusal is an HTTP 200 that can still carry partial text, so it has to be recognised
/// before the text is read - otherwise the fragment is parsed as an answer, fails, and is retried.
/// </summary>
public sealed class AnthropicMessagesTransportTests
{
    private const string Answer = """{"content":[{"type":"text","text":"hello"}],"stop_reason":"end_turn"}""";

    private const string Refusal =
        """{"content":[{"type":"text","text":"{\"high_level"}],"stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber","explanation":"This request was declined by a safety classifier."}}""";

    [Fact]
    public async Task ItSpeaksApiVersion2026_10_21()
    {
        var http = new CannedHandler(Answer);

        await Send(http);

        http.Versions.Should().Equal("2026-10-21");
    }

    [Fact]
    public async Task AnAnswerReturnsItsText()
    {
        (await Send(new CannedHandler(Answer))).Should().Be("hello");
    }

    [Fact]
    public async Task ARefusalThrowsWithTheCategoryAndExplanation_AndNeverReturnsThePartialText()
    {
        var act = () => Send(new CannedHandler(Refusal));

        var thrown = (await act.Should().ThrowAsync<LlmRefusalException>()).Which;
        thrown.Category.Should().Be("cyber");
        thrown.Message.Should().Contain("Anthropic declined").And.Contain("safety classifier");
    }

    [Fact]
    public async Task ARefusalWithoutDetailsStillSaysWhatHappened()
    {
        var act = () => Send(new CannedHandler("""{"content":[],"stop_reason":"refusal"}"""));

        (await act.Should().ThrowAsync<LlmRefusalException>())
            .Which.Message.Should().Be("Anthropic declined this request. Rephrase it, or pick another model.");
    }

    [Fact]
    public async Task AServiceReportsARefusalOnceAndDoesNotRetryIt()
    {
        // Retrying a refusal meets the same classifier and bills the call twice; the old path read the
        // partial text as broken JSON and asked again.
        var http = new CannedHandler(Refusal);
        var sut = new CaptionMutationLlmService(
            KeyStore<IAnthropicTokenStore>("sk-ant-test"),
            KeyStore<IOpenRouterTokenStore>("sk-or-test"),
            NullLogger<CaptionMutationLlmService>.Instance,
            new StubHttpClientFactory(http));
        var baseCaption = V4JsonPromptSerializer.Deserialize(
            """{"high_level_description":"A red fox in snow","compositional_deconstruction":{"background":"a snowy field","elements":[{"type":"obj","desc":"a red fox"}]}}""");

        var result = await sut.MutateAsync(baseCaption, "make it winter", 0,
            new LlmTarget(ModelProvider.Anthropic, "claude-opus-5-5", "http://127.0.0.1:11434"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("declined").And.NotContain("couldn't reach");
        http.Versions.Should().HaveCount(1, "a refusal is final for that request");
    }

    private static Task<string> Send(CannedHandler http) =>
        AnthropicMessagesTransport.SendAsync(
            new StubHttpClientFactory(http), NullLogger.Instance, "claude-opus-5-5", "sk-ant-test",
            "system", [new ChatTurn("user", "hi")], schema: null, CancellationToken.None);

    private static T KeyStore<T>(string key) where T : class, ITokenStore
    {
        var mock = new Mock<T>();
        mock.Setup(s => s.LoadAsync()).ReturnsAsync(key);
        return mock.Object;
    }

    private sealed class CannedHandler(string body) : HttpMessageHandler
    {
        public List<string> Versions { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Versions.AddRange(request.Headers.TryGetValues("anthropic-version", out var v) ? v : ["<none>"]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
