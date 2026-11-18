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

    // --- request-body logging (Replicate only; see RemoteHttpLoggingHandler) -----------
    // The reason the body is logged at all: which input fields went out decides both whether a
    // Replicate call works and what it costs, and neither the URL nor the status code records it.
    // The reason these tests exist: the same line is a secret-and-image leak unless it is scrubbed,
    // and reading a body in a handler is a easy way to silently send an empty request.

    [Fact]
    public async Task SendAsync_LogsTheRequestBody_WhenTheClientAsksForIt()
    {
        var logger = new CapturingLogger<RemoteHttpLoggingHandler>();
        var handler = new RemoteHttpLoggingHandler(logger, "IReplicateApi", logRequestBody: true)
        {
            InnerHandler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.Created))
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/models/openai/gpt-image-2.6-flare/predictions");
        request.Content = new StringContent(
            """{"input":{"prompt":"a cat","aspect_ratio":"9:16","complexity":"low"}}""");

        using var response = await client.SendAsync(request);

        var joined = string.Join("\n", logger.Messages);
        joined.Should().Contain("\"aspect_ratio\":\"9:16\"")
            .And.Contain("\"complexity\":\"low\"");
    }

    [Fact]
    public async Task SendAsync_BodyLogging_KeepsTheRequestSendableAndScrubbed()
    {
        // Three failure modes at once: buffering the body to log it must not consume it, and the
        // line must carry neither the account token nor the attached image.
        string? arrived = null;
        var logger = new CapturingLogger<RemoteHttpLoggingHandler>();
        var handler = new RemoteHttpLoggingHandler(logger, "IReplicateApi", logRequestBody: true)
        {
            InnerHandler = new RecordingHandler(body => arrived = body, HttpStatusCode.Created)
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/models/x/y/predictions");
        request.Content = new StringContent(
            "{\"input\":{\"api_token\":\"r8_Sup3rS3cr3tValue\",\"prompt\":\"a cat\",\"image\":\"data:image/png;base64,"
            + new string('A', 400_000) + "\"}}");

        using var response = await client.SendAsync(request);

        var joined = string.Join("\n", logger.Messages);
        arrived.Should().NotBeNull().And.Contain("a cat");
        joined.Should().Contain("\"prompt\":\"a cat\"");
        joined.Should().NotContain("r8_Sup3rS3cr3tValue").And.NotContain("data:image/png");
        joined.Should().Contain("<inline image");
    }

    [Fact]
    public async Task SendAsync_BodyLogging_SaysHowMuchItLeftOut()
    {
        var logger = new CapturingLogger<RemoteHttpLoggingHandler>();
        var handler = new RemoteHttpLoggingHandler(logger, "IReplicateApi", logRequestBody: true)
        {
            InnerHandler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.Created))
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.replicate.com/v1/models/x/y/predictions");
        // A body long enough to need cutting is the realistic shape: several fields, each short.
        request.Content = new StringContent(
            """{"input":{"prompt":"a cat","aspect_ratio":"9:16","fields":{"""
            + string.Join(",", Enumerable.Range(0, 200).Select(i => $"\"field_{i}\":\"value_{i}\""))
            + """}}}""");

        using var response = await client.SendAsync(request);

        var line = logger.Messages.Single(m => m.Contains("HTTP outbound start"));
        // A cut that announces its own size cannot be mistaken for the whole request, and it stops on
        // a field boundary rather than inside an identifier.
        line.Should().MatchRegex(@"… \(\+\d+ chars\)$");
        line.Should().NotMatchRegex(@"field_\d+…", "the cut must not stop mid-value");
        line.Should().Contain("\"aspect_ratio\":\"9:16\"", "the head of a request is the useful half");
    }

    [Fact]
    public async Task SendAsync_WithoutBodyLogging_LogsNoBodyForGets()
    {
        var logger = new CapturingLogger<RemoteHttpLoggingHandler>();
        var handler = new RemoteHttpLoggingHandler(logger, "IReplicateApi", logRequestBody: true)
        {
            InnerHandler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK))
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.replicate.com/v1/models");

        using var response = await client.SendAsync(request);

        logger.Messages.Single(m => m.Contains("HTTP outbound start")).Should().Contain("Body=-");
    }

    private sealed class RecordingHandler(Action<string> recordBody, HttpStatusCode status) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                recordBody(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status);
        }
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
