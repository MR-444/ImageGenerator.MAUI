using System.Text.Json;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

/// <summary>Reads ComfyUI's native model-file catalog used by LoraLoaderModelOnly.</summary>
public sealed class ComfyUiLoraService : IComfyUiLoraService
{
    internal const string HttpClientName = "comfyui-lora-catalog";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUiStateStore _uiStateStore;
    private readonly IComfyUiAuthStore _authStore;
    private readonly ILogger<ComfyUiLoraService> _logger;

    public ComfyUiLoraService(
        IHttpClientFactory httpClientFactory,
        IUiStateStore uiStateStore,
        IComfyUiAuthStore authStore,
        ILogger<ComfyUiLoraService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> GetLoraNamesAsync(CancellationToken ct = default)
    {
        var baseUrl = _uiStateStore.LoadComfyUiBaseUrl() ?? ModelConstants.ComfyUi.DefaultBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException($"The ComfyUI server URL '{baseUrl}' is not valid.");

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        ComfyUiAuthHeader.Apply(client, await _authStore.LoadAsync());

        var endpoint = new Uri(baseUri, "models/loras");
        using var response = await client.GetAsync(endpoint, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "ComfyUI LoRA catalog HTTP {StatusCode} Url={Url}",
                (int)response.StatusCode, endpoint);
            throw new InvalidOperationException(
                $"ComfyUI returned HTTP {(int)response.StatusCode} listing LoRAs.");
        }

        List<string>? names;
        try
        {
            names = JsonSerializer.Deserialize<List<string>>(body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "ComfyUI LoRA catalog returned invalid JSON");
            throw new InvalidOperationException("ComfyUI returned an invalid LoRA catalog.", ex);
        }

        return (names ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
