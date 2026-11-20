namespace ImageGenerator.MAUI.Infrastructure.External.Ollama;

/// <summary>
/// The configured address <em>answered</em>, but not the way Ollama does: an HTTP failure on
/// <c>/api/tags</c>, a body that is not JSON, or JSON without Ollama's <c>models</c> array.
///
/// Kept distinct from a transport failure on purpose. "Couldn't reach Ollama at X" sends the user
/// after a dead server that is demonstrably alive: an OpenAI-compatible host (any <c>/v1</c> root,
/// a gateway, a proxy) answers any port you point at it, and simply has no <c>/api/tags</c> route.
/// Before this type existed both cases raised the same <see cref="InvalidOperationException"/>, so
/// the settings card could only print one sentence and it was wrong for the common case.
///
/// Facts up, prose down: this carries the observation, and the view model that has to fit it into a
/// label owns the wording.
/// </summary>
public sealed class OllamaEndpointException(
    string endpoint,
    int? statusCode,
    string? problem = null,
    Exception? inner = null)
    : Exception(Describe(endpoint, statusCode, problem), inner)
{
    /// <summary>The URL that was called, e.g. <c>http://host:47832/v1/api/tags</c>.</summary>
    public string Endpoint { get; } = endpoint;

    /// <summary>HTTP status the host returned; null only when no response was ever produced.</summary>
    public int? StatusCode { get; } = statusCode;

    /// <summary>
    /// What came back that Ollama would never send, or null when the status code already says it all.
    /// </summary>
    public string? Problem { get; } = problem;

    private static string Describe(string endpoint, int? statusCode, string? problem) =>
        statusCode is { } code
            ? problem is null
                ? $"GET {endpoint} returned HTTP {code}."
                : $"GET {endpoint} returned HTTP {code}: {problem}"
            : $"GET {endpoint}: {problem}";
}
