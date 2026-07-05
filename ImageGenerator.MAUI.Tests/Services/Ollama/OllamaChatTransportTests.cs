using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.External.Ollama;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Services.Ollama;

public sealed class OllamaChatTransportTests
{
    [Fact]
    public async Task SendAsync_SendsThinkFalse()
    {
        var handler = new CaptureBodyHandler();
        var sut = new StubHttpClientFactory(new HttpClient(handler));

        var result = await OllamaChatTransport.SendAsync(
            sut,
            NullLogger.Instance,
            "http://host:11434",
            "qwen3-vl:32b",
            "system",
            [new ChatTurn("user", "hello")],
            schema: null,
            CancellationToken.None);

        result.Should().Be("ok");
        using var doc = JsonDocument.Parse(handler.Body);
        doc.RootElement.GetProperty("think").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_EmptyContentWithThinking_ThrowsHelpfulError()
    {
        var handler = new CaptureBodyHandler("""{"message":{"content":"","thinking":"reasoning only"},"done":true,"done_reason":"length"}""");
        var sut = new StubHttpClientFactory(new HttpClient(handler));

        var act = async () => await OllamaChatTransport.SendAsync(
            sut,
            NullLogger.Instance,
            "http://host:11434",
            "qwen3-vl:32b",
            "system",
            [new ChatTurn("user", "hello")],
            schema: null,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*non-thinking/instruct model tag*");
    }

    private sealed class CaptureBodyHandler(
        string responseJson = """{"message":{"content":"ok"},"done":true}""") : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        }
    }
}
