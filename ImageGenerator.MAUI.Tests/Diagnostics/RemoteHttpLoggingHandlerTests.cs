using System.Net;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Tests.Diagnostics;

public sealed class RemoteHttpLoggingHandlerTests
{
    [Fact]
    public async Task SendAsync_LogsStartAndEnd_WithoutHeadersQueryOrBody()
    {
        var logger = new CapturingLogger<RemoteHttpLoggingHandler>();
        var handler = new RemoteHttpLoggingHandler(logger, "openrouter")
        {
            InnerHandler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK))
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions?api_key=secret");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");
        request.Content = new StringContent("prompt text and base64 image");
        request.Options.Set(RemoteHttpLoggingHandler.PurposeKey, "remote image observation");
        request.Options.Set(RemoteHttpLoggingHandler.ModelKey, "free/vision:free");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        logger.Messages.Should().Contain(m => m.Contains("HTTP outbound start"));
        logger.Messages.Should().Contain(m => m.Contains("HTTP outbound end"));
        var joined = string.Join("\n", logger.Messages);
        joined.Should().Contain("openrouter");
        joined.Should().Contain("remote image observation");
        joined.Should().Contain("free/vision:free");
        joined.Should().NotContain("api_key");
        joined.Should().NotContain("secret");
        joined.Should().NotContain("prompt text");
        joined.Should().NotContain("base64");
    }

    [Fact]
    public async Task SendAsync_LogsFailure_WithoutLeakingBody()
    {
        var logger = new CapturingLogger<RemoteHttpLoggingHandler>();
        var handler = new RemoteHttpLoggingHandler(logger, "anthropic")
        {
            InnerHandler = new ThrowingHandler(new InvalidOperationException("network down"))
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Content = new StringContent("expensive prompt");

        var act = async () => await client.SendAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Messages.Should().Contain(m => m.Contains("HTTP outbound failed"));
        string.Join("\n", logger.Messages).Should().NotContain("expensive prompt");
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
