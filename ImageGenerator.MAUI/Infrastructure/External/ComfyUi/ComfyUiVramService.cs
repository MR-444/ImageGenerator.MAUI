using System.Text;
using System.Text.Json.Nodes;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

/// <summary>
/// Frees the ComfyUI server's GPU memory via its built-in <c>POST /free</c> endpoint. Resolves the base
/// URL and auth header per call exactly like <see cref="ComfyUiImageGenerationService"/>, and shares the
/// same <c>"comfyui"</c> named client. Best-effort: a short timeout and a swallow-all catch — mirrors the
/// cancel-notify path, since freeing VRAM must never surface an error or stall the UI.
/// </summary>
public sealed class ComfyUiVramService : IComfyUiVramService
{
    // A dead/slow host must not hang the post-render path; the shared resilience pipeline would allow minutes.
    private static readonly TimeSpan FreeTimeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUiStateStore _uiStateStore;
    private readonly IComfyUiAuthStore _authStore;
    private readonly ILogger<ComfyUiVramService> _logger;

    public ComfyUiVramService(
        IHttpClientFactory httpClientFactory,
        IUiStateStore uiStateStore,
        IComfyUiAuthStore authStore,
        ILogger<ComfyUiVramService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task TryFreeAsync(CancellationToken ct = default)
    {
        try
        {
            var baseUrl = _uiStateStore.LoadComfyUiBaseUrl() ?? ModelConstants.ComfyUi.DefaultBaseUrl;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                _logger.LogWarning("ComfyUI free skipped: invalid server URL '{BaseUrl}'", baseUrl);
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(FreeTimeout);

            var authHeader = await _authStore.LoadAsync();
            using var httpClient = _httpClientFactory.CreateClient(ComfyUiImageGenerationService.HttpClientName);
            ComfyUiAuthHeader.Apply(httpClient, authHeader);

            var body = new JsonObject { ["unload_models"] = true, ["free_memory"] = true };
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(new Uri(baseUri, "free"), content, cts.Token);
            _logger.LogInformation("ComfyUI free requested (HTTP {StatusCode})", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI free failed");
        }
    }
}
