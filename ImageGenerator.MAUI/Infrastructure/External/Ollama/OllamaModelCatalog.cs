using System.Text.Json;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Ollama;

/// <summary>
/// Reads <c>GET {baseUrl}/api/tags</c> on the local Ollama server and returns the installed model tags.
/// Uses the shared "ollama" named client (so it inherits the generous timeout/retry).
/// </summary>
public sealed class OllamaModelCatalog : IOllamaModelCatalog
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaModelCatalog> _logger;

    public OllamaModelCatalog(IHttpClientFactory httpClientFactory, ILogger<OllamaModelCatalog> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("No Ollama server URL is set.");

        var endpoint = $"{baseUrl.TrimEnd('/')}/api/tags";
        using var client = _httpClientFactory.CreateClient(OllamaChatTransport.HttpClientName);
        using var response = await client.GetAsync(endpoint, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Ollama /api/tags HTTP {StatusCode} Url={Url}", (int)response.StatusCode, endpoint);
            throw new InvalidOperationException($"Ollama returned HTTP {(int)response.StatusCode} listing models.");
        }

        using var doc = JsonDocument.Parse(body);
        var names = new List<string>();
        if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in models.EnumerateArray())
            {
                if (model.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } n)
                    names.Add(n);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }
}
