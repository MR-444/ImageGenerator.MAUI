using FluentAssertions;
using ImageGenerator.MAUI.Extensions;
using Microsoft.Extensions.DependencyInjection;
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
                // Refit 10's DefaultApiExceptionFactory dereferences this when status != 2xx.
                RequestMessage = request
            };
            return Task.FromResult(response);
        }
    }
}
