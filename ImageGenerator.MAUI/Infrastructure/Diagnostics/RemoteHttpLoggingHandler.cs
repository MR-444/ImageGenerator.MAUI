using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.Diagnostics;

/// <summary>
/// Logs remote HTTP calls without recording secrets, prompts, image payloads, request bodies, or response bodies.
/// Attach only to internet-facing clients; local Ollama/ComfyUI clients intentionally stay out.
/// </summary>
public sealed class RemoteHttpLoggingHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<string> PurposeKey = new("Emberforge.RemoteHttpPurpose");
    public static readonly HttpRequestOptionsKey<string> ModelKey = new("Emberforge.RemoteHttpModel");

    private readonly ILogger<RemoteHttpLoggingHandler> _logger;
    private readonly string _clientName;

    public RemoteHttpLoggingHandler(ILogger<RemoteHttpLoggingHandler> logger, string clientName)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? "remote" : clientName;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var url = Sanitize(request.RequestUri);
        var purpose = Option(request, PurposeKey);
        var model = Option(request, ModelKey);

        _logger.LogInformation(
            "HTTP outbound start Client={Client} Method={Method} Url={Url} Purpose={Purpose} Model={Model}",
            _clientName, method, url, purpose, model);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            sw.Stop();
            _logger.LogInformation(
                "HTTP outbound end Client={Client} Method={Method} Url={Url} Status={StatusCode} ElapsedMs={ElapsedMs} Purpose={Purpose} Model={Model}",
                _clientName, method, url, (int)response.StatusCode, sw.ElapsedMilliseconds, purpose, model);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                ex,
                "HTTP outbound failed Client={Client} Method={Method} Url={Url} ElapsedMs={ElapsedMs} Failure={Failure} Purpose={Purpose} Model={Model}",
                _clientName, method, url, sw.ElapsedMilliseconds, ex.GetType().Name, purpose, model);
            throw;
        }
    }

    private static string Option(HttpRequestMessage request, HttpRequestOptionsKey<string> key) =>
        request.Options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "-";

    private static string Sanitize(Uri? uri)
    {
        if (uri is null)
            return "(null)";

        if (!uri.IsAbsoluteUri)
            return uri.ToString();

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        };
        return builder.Uri.ToString();
    }
}
