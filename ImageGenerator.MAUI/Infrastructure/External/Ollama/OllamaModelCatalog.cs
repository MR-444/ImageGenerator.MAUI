using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Ollama;

/// <summary>
/// Reads <c>GET {baseUrl}/api/tags</c> on the local Ollama server and returns the installed model tags.
/// Uses a short-timeout named client: refresh should fail quickly when Ollama is offline, unlike
/// generation calls that need a generous cold-load budget.
/// </summary>
public sealed class OllamaModelCatalog : IOllamaModelCatalog
{
    public const string HttpClientName = "ollama-catalog";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaModelCatalog> _logger;

    public OllamaModelCatalog(IHttpClientFactory httpClientFactory, ILogger<OllamaModelCatalog> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, CancellationToken ct = default)
    {
        var infos = await ListModelInfosAsync(baseUrl, ct);
        return infos.Select(i => i.Name).ToArray();
    }

    public async Task<IReadOnlyList<OllamaModelInfo>> ListModelInfosAsync(string baseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("No Ollama server URL is set.");

        var endpoint = $"{baseUrl.TrimEnd('/')}/api/tags";
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(endpoint, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Ollama /api/tags HTTP {StatusCode} Url={Url}", (int)response.StatusCode, endpoint);
            throw new InvalidOperationException($"Ollama returned HTTP {(int)response.StatusCode} listing models.");
        }

        using var doc = JsonDocument.Parse(body);
        var infos = new List<OllamaModelInfo>();
        if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in models.EnumerateArray())
            {
                if (model.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } n)
                {
                    var capabilities = new List<string>();
                    if (model.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cap in caps.EnumerateArray())
                        {
                            if (cap.GetString() is { Length: > 0 } c)
                                capabilities.Add(c);
                        }
                    }

                    infos.Add(new OllamaModelInfo(n, capabilities));
                }
            }
        }

        infos.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return await ConfirmVisionAsync(baseUrl, infos, ct);
    }

    // How many /api/show probes to run at once. Refresh is a manual action and /api/show is a cheap
    // metadata read, but we cap concurrency so a long model list doesn't open a burst of sockets.
    private const int VisionProbeConcurrency = 5;

    /// <summary>
    /// Ollama's <c>/api/tags</c> under-reports the <c>vision</c> capability for some model families
    /// (notably gemma3/gemma4), while <c>/api/show</c> reports it authoritatively. For every model
    /// <c>/api/tags</c> did NOT already flag as vision, confirm via a <c>/api/show</c> metadata read
    /// (which does not load the model into VRAM) and graft the <c>vision</c> capability on when present.
    /// Models already flagged skip the probe, so this self-heals to zero extra calls if Ollama ever
    /// fixes <c>/api/tags</c>. Per-model failures fall back to the <c>/api/tags</c> capabilities.
    /// </summary>
    private async Task<IReadOnlyList<OllamaModelInfo>> ConfirmVisionAsync(
        string baseUrl, IReadOnlyList<OllamaModelInfo> infos, CancellationToken ct)
    {
        var endpoint = $"{baseUrl.TrimEnd('/')}/api/show";
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var gate = new SemaphoreSlim(VisionProbeConcurrency);

        var probes = infos.Select(async info =>
        {
            if (info.SupportsVision) return info; // /api/tags already reported it — trust it.

            await gate.WaitAsync(ct);
            try
            {
                var body = new JsonObject { ["model"] = info.Name };
                using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(endpoint, content, ct);
                if (!response.IsSuccessStatusCode) return info;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("capabilities", out var caps)
                    && caps.ValueKind == JsonValueKind.Array
                    && caps.EnumerateArray().Any(c =>
                        string.Equals(c.GetString(), "vision", StringComparison.OrdinalIgnoreCase)))
                {
                    return info with { Capabilities = [.. info.Capabilities, "vision"] };
                }

                return info;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ollama /api/show vision-probe failed Model={Model}", info.Name);
                return info;
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(probes);
    }

    // Short budget: a dead/slow host must not stall the mutation→render handoff. The unload is a
    // free-the-GPU nicety, so a failure is logged and swallowed.
    private static readonly TimeSpan UnloadTimeout = TimeSpan.FromSeconds(10);

    public async Task UnloadAsync(string baseUrl, string model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
            return;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(UnloadTimeout);

            // Ollama unloads a model when it receives a request with keep_alive 0; an empty prompt means
            // "just (un)load", no generation. Native /api/generate, not the OpenAI-compatible endpoint.
            var endpoint = $"{baseUrl.TrimEnd('/')}/api/generate";
            var body = new JsonObject { ["model"] = model, ["keep_alive"] = 0 };
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var client = _httpClientFactory.CreateClient(OllamaChatTransport.HttpClientName);
            using var response = await client.PostAsync(endpoint, content, cts.Token);
            _logger.LogInformation(
                "Ollama unload requested Model={Model} (HTTP {StatusCode})", model, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama unload failed Model={Model}", model);
        }
    }
}
