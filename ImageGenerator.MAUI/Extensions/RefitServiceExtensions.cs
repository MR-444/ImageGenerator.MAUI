using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Refit;

namespace ImageGenerator.MAUI.Extensions;

public static class RefitServiceExtensions
{
    // Replicate holds connections up to ~60s on `Prefer: wait`; OpenAI generations are typically <30s
    // but occasionally slow. Pick attempt/total timeouts that accommodate the longer of the two.
    private static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromMinutes(5);

    public static IHttpClientBuilder AddRefitClient<T>(this IServiceCollection services, string baseAddress) where T : class
    {
        var builder = services
            .AddRefitClient<T>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(baseAddress);
                client.Timeout = TotalRequestTimeout;
            });

        builder.AddResilienceHandler("standard", pipeline =>
        {
            pipeline.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = TotalRequestTimeout,
                Name = "TotalRequestTimeout"
            });

            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(2),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>()
                    .HandleResult(response =>
                        response.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                        response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                        (int)response.StatusCode >= 500)
            });

            pipeline.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = PerAttemptTimeout,
                Name = "PerAttemptTimeout"
            });
        });

        return builder;
    }
}
