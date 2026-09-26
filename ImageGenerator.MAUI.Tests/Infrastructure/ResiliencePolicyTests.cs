using FluentAssertions;
using ImageGenerator.MAUI.Extensions;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using Refit;
using System.Net;

namespace ImageGenerator.MAUI.Tests.Infrastructure;

public class ResiliencePolicyTests
{
    public interface ITestApi
    {
        [Get("/ping")]
        Task<string> PingAsync(CancellationToken cancellationToken = default);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task ResiliencePipeline_RetriesUpTo3TimesOnTransientFailures(HttpStatusCode status)
    {
        var handler = new CountingHandler(alwaysReturn: status);
        var services = BuildServices(handler);
        var api = services.GetRequiredService<ITestApi>();

        var act = () => api.PingAsync();
        await act.Should().ThrowAsync<ApiException>();

        handler.CallCount.Should().Be(4, "1 initial attempt + 3 retries on transient failures");
    }

    [Fact]
    public async Task ResiliencePipeline_StopsRetryingOnceSuccessResponseReturns()
    {
        var handler = new CountingHandler(new Queue<HttpStatusCode>(new[]
        {
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK
        }));
        var services = BuildServices(handler);
        var api = services.GetRequiredService<ITestApi>();

        var result = await api.PingAsync();

        result.Should().Be("pong");
        handler.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task ResiliencePipeline_DoesNotRetryOn4xxClientError()
    {
        var handler = new CountingHandler(alwaysReturn: HttpStatusCode.BadRequest);
        var services = BuildServices(handler);
        var api = services.GetRequiredService<ITestApi>();

        var act = () => api.PingAsync();
        await act.Should().ThrowAsync<ApiException>();

        handler.CallCount.Should().Be(1, "4xx responses other than 408/429 are non-transient");
    }

    [Fact]
    public async Task GeneratedReplicateClient_SendsPredictionRequest()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services
            .AddRefitClient<IReplicateApi>("https://api.replicate.com")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var api = provider.GetRequiredService<IReplicateApi>();
        var response = await api.CreatePredictionAsync(
            "Bearer test-token", "model-name",
            new ReplicatePredictionRequest { Input = new { prompt = "hello" } });

        response.Id.Should().Be("prediction-id");
        handler.RequestUri.Should().Be("https://api.replicate.com/v1/models/model-name/predictions");
        handler.Authorization.Should().Be("Bearer test-token");
        handler.Body.Should().Be("{\"input\":{\"prompt\":\"hello\"}}");
    }

    [Fact]
    public async Task ResiliencePipeline_WrapsTransportFailuresAsApiRequestException()
    {
        var handler = new ThrowingHandler();
        using var services = BuildServices(handler);
        var api = services.GetRequiredService<ITestApi>();

        var act = () => api.PingAsync();
        var exception = await act.Should().ThrowAsync<ApiRequestException>();

        exception.Which.InnerException.Should().BeOfType<HttpRequestException>()
            .Which.Message.Should().Be("network down");
        handler.CallCount.Should().Be(4, "transport failures are retried three times before Refit wraps them");
    }

    [Fact]
    public async Task ResiliencePipeline_PreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var services = BuildServices(new CancelingHandler(cancellation));
        var api = services.GetRequiredService<ITestApi>();

        var act = () => api.PingAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ServiceProvider BuildServices(HttpMessageHandler primaryHandler)
    {
        var services = new ServiceCollection();
        services
            .AddRefitClient<ITestApi>("http://localhost", retryDelay: TimeSpan.FromMilliseconds(1))
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);
        return services.BuildServiceProvider();
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode>? _queue;
        private readonly HttpStatusCode? _alwaysReturn;
        public int CallCount { get; private set; }

        public CountingHandler(HttpStatusCode alwaysReturn)
        {
            _alwaysReturn = alwaysReturn;
        }

        public CountingHandler(Queue<HttpStatusCode> queue)
        {
            _queue = queue;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var status = _alwaysReturn ?? (_queue!.Count > 0 ? _queue.Dequeue() : HttpStatusCode.OK);
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent("pong"),
                // Preserve the originating request for Refit's error diagnostics.
                RequestMessage = request
            };
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":\"prediction-id\",\"status\":\"starting\"}"),
                RequestMessage = request
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("network down"));
        }
    }

    private sealed class CancelingHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }
}
