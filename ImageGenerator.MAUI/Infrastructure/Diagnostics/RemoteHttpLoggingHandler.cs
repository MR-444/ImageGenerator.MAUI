using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.Diagnostics;

/// <summary>
/// Logs remote HTTP calls without recording secrets, query strings, headers, or response bodies.
/// Attach only to internet-facing clients; local Ollama/ComfyUI clients intentionally stay out.
///
/// Request bodies are off by default and logged only for clients built with <c>logRequestBody</c>
/// (Replicate today). Prompts therefore reach the log file for those clients — that is the point of
/// recording a request: "which fields did we actually send" is unanswerable from a URL, and the
/// field set is what decides both whether a call works and what it costs. Inline images are replaced
/// by their size and token-shaped values are redacted before anything is written.
/// </summary>
public sealed class RemoteHttpLoggingHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<string> PurposeKey = new("Emberforge.RemoteHttpPurpose");
    public static readonly HttpRequestOptionsKey<string> ModelKey = new("Emberforge.RemoteHttpModel");

    /// <summary>
    /// Bodies are JSON with a long prompt and an enumerated vocabulary; the useful part is the head.
    /// The cut says how much it left out, so a truncated line can't be mistaken for the whole request.
    /// </summary>
    private const int MaxLoggedBodyLength = 1500;

    // data:image/png;base64,iVBOR... — the only thing a generation request shares with the log that
    // must never survive: a megabyte of base64 per attached image, and the image content itself.
    private static readonly Regex InlineImage = new("data:[^\"'\\\\]{0,200}?base64,[A-Za-z0-9+/=]+", RegexOptions.Compiled);

    // Replicate's api_token, and any provider's key when a service puts it in the body instead of
    // the header. Matching on shape and on key name, since a body is not allowed to carry either.
    private static readonly Regex TokenKeyValue = new("\"(?:api_?token|token|api_?key|access_token|key)\"\\s*:\\s*\"[^\"]*\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReplicateToken = new("r8_[A-Za-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex BearerValue = new("Bearer\\s+[^\"'\\s,}]+", RegexOptions.Compiled);

    private readonly ILogger<RemoteHttpLoggingHandler> _logger;
    private readonly string _clientName;
    private readonly bool _logRequestBody;

    public RemoteHttpLoggingHandler(ILogger<RemoteHttpLoggingHandler> logger, string clientName, bool logRequestBody = false)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clientName = string.IsNullOrWhiteSpace(clientName) ? "remote" : clientName;
        _logRequestBody = logRequestBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method.Method;
        var url = Sanitize(request.RequestUri);
        var purpose = Option(request, PurposeKey);
        var model = Option(request, ModelKey);
        var body = _logRequestBody && !HttpMethod.Get.Equals(request.Method)
            ? await DescribeBodyAsync(request, cancellationToken)
            : "-";

        _logger.LogInformation(
            "HTTP outbound start Client={Client} Method={Method} Url={Url} Purpose={Purpose} Model={Model} Body={Body}",
            _clientName, method, url, purpose, model, body);

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

    /// <summary>
    /// Reads the outgoing body for the log without consuming it: <see cref="HttpContent.LoadIntoBufferAsync"/>
    /// parks the bytes so <c>base.SendAsync</c> still has something to send. Never throws — a logging
    /// failure must not cost the user a generation they already paid for.
    /// </summary>
    private static async Task<string> DescribeBodyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null) return "-";
        try
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            var text = await request.Content.ReadAsStringAsync(cancellationToken);
            return text.Length == 0 ? "-" : Bound(Sanitize(text));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"(unreadable: {ex.GetType().Name})";
        }
    }

    private static string Sanitize(string body)
    {
        body = InlineImage.Replace(body, m => $"<inline image {m.Length / 1024} KB>");
        body = TokenKeyValue.Replace(body, "\"token\":\"[redacted]\"");
        body = ReplicateToken.Replace(body, "[redacted]");
        return BearerValue.Replace(body, "Bearer [redacted]");
    }

    private static string Bound(string body)
    {
        if (body.Length <= MaxLoggedBodyLength) return body;
        // Not a quote: in JSON a quote closes both keys and values, and the one after a key is
        // followed by ":" — cutting there leaves "...\"field_66 on the line, which is worse than a
        // shorter line. Commas are the only real field boundary.
        var cut = body.LastIndexOfAny([',', ';', ' '], MaxLoggedBodyLength);
        if (cut < MaxLoggedBodyLength / 2) cut = MaxLoggedBodyLength;
        return $"{body[..cut].TrimEnd(',', ' ')}… (+{body.Length - cut} chars)";
    }

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
