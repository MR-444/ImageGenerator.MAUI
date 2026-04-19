using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    // Replicate rejects payloads that send optional fields as `null` (HTTP 422 "expected string, given null").
    // Skip nulls globally so every DTO's optional properties drop out of the wire format.
    // `WhenWritingNull` handles POCO properties; `NullSkippingDictionaryConverter` handles
    // `Dictionary<string, object?>` payloads (the factory uses these for Flux 2 / gpt-image-1.5 / fallback).
    public static JsonSerializerOptions CreateContentSerializerOptions() =>
        // Web defaults give us camelCase naming + case-insensitive property matching —
        // matches Refit's built-in default so unattributed properties (like
        // ReplicatePredictionRequest.Input) serialize as "input" not "Input".
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new NullSkippingDictionaryConverter() }
        };

    private static readonly RefitSettings SharedRefitSettings = new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(CreateContentSerializerOptions())
    };

    internal sealed class NullSkippingDictionaryConverter : JsonConverter<Dictionary<string, object?>>
    {
        public override Dictionary<string, object?>? Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<Dictionary<string, object?>>(ref reader, WithoutSelf(options));

        public override void Write(
            Utf8JsonWriter writer, Dictionary<string, object?> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var kvp in value)
            {
                if (kvp.Value is null) continue;
                writer.WritePropertyName(kvp.Key);
                JsonSerializer.Serialize(writer, kvp.Value, options);
            }
            writer.WriteEndObject();
        }

        // Deserialize without this converter in the chain to avoid recursion.
        private static JsonSerializerOptions WithoutSelf(JsonSerializerOptions options)
        {
            var copy = new JsonSerializerOptions(options);
            for (var i = copy.Converters.Count - 1; i >= 0; i--)
            {
                if (copy.Converters[i] is NullSkippingDictionaryConverter) copy.Converters.RemoveAt(i);
            }
            return copy;
        }
    }

    public static IHttpClientBuilder AddRefitClient<T>(
        this IServiceCollection services,
        string baseAddress,
        TimeSpan? retryDelay = null) where T : class
    {
        var delay = retryDelay ?? TimeSpan.FromSeconds(2);

        var builder = services
            .AddRefitClient<T>(SharedRefitSettings)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(baseAddress);
                client.Timeout = TotalRequestTimeout;
            })
            // Replicate/OpenAI send gzip/brotli-compressed JSON responses. Without automatic
            // decompression on the primary handler, Refit reads raw bytes that fail to
            // deserialize, surfacing as "HTTP 2xx: (no body)" ApiExceptions.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
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
                Delay = delay,
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
