using System.Text.Json;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

/// <summary>
/// Fetches the server's installed checkpoints via GET /object_info/CheckpointLoaderSimple —
/// the narrow per-class endpoint (a few KB) instead of the full /object_info dump (multi-MB
/// on large installs). Successful fetches rewrite a disk cache so the picker stays populated
/// while the server is offline; cache conventions mirror ModelCatalogService.
/// </summary>
public sealed class ComfyUiCheckpointService : IComfyUiCheckpointService
{
    private const string CacheFileName = "comfyui-checkpoints.json";

    // The shared "comfyui" resilience pipeline allows up to 3 minutes (sized for renders);
    // this fetch runs on model select, so a dead LAN host must fail fast into the cache.
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUiStateStore _uiStateStore;
    private readonly ILogger<ComfyUiCheckpointService> _logger;
    private readonly string _cacheDirectory;
    private readonly string _workflowsDirectory;

    public ComfyUiCheckpointService(
        IHttpClientFactory httpClientFactory,
        IUiStateStore uiStateStore,
        ILogger<ComfyUiCheckpointService> logger,
        string? cacheDirectoryOverride = null,
        string? workflowsDirectoryOverride = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheDirectory = cacheDirectoryOverride ?? FileSystem.AppDataDirectory;
        _workflowsDirectory = workflowsDirectoryOverride ?? OutputPaths.ComfyWorkflowsDirectory;
    }

    public async Task<IReadOnlyList<string>?> GetCheckpointsAsync(CancellationToken ct = default)
    {
        var baseUrl = _uiStateStore.LoadComfyUiBaseUrl() ?? ModelConstants.ComfyUi.DefaultBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            _logger.LogWarning("ComfyUI checkpoint fetch skipped: invalid base URL '{BaseUrl}'", baseUrl);
            return await LoadCachedAsync(ct);
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient(ComfyUiImageGenerationService.HttpClientName);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(FetchTimeout);

            using var response = await httpClient.GetAsync(
                new Uri(baseUri, "object_info/CheckpointLoaderSimple"), timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);

            var names = ParseCheckpointNames(doc);
            if (names is null)
            {
                _logger.LogWarning("ComfyUI /object_info/CheckpointLoaderSimple had an unexpected shape");
                return await LoadCachedAsync(ct);
            }

            _logger.LogInformation("ComfyUI checkpoints fetched Count={Count}", names.Count);
            await SaveCachedAsync(names, ct);
            return names;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI checkpoint fetch failed, falling back to cache");
            return await LoadCachedAsync(ct);
        }
    }

    public async Task<string?> GetWorkflowCheckpointAsync(string workflowName, CancellationToken ct = default)
    {
        try
        {
            var path = Path.Combine(_workflowsDirectory, workflowName + ".json");
            if (!File.Exists(path)) return null;

            var template = await File.ReadAllTextAsync(path, ct);
            return ComfyUiWorkflowPatcher.FindBakedCheckpoint(template);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI workflow checkpoint probe failed Workflow={Workflow}", workflowName);
            return null;
        }
    }

    /// <summary>Drills CheckpointLoaderSimple → input → required → ckpt_name → [0]; null on any mismatch.</summary>
    private static List<string>? ParseCheckpointNames(JsonDocument doc)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("CheckpointLoaderSimple", out var cls)
            || !cls.TryGetProperty("input", out var input)
            || !input.TryGetProperty("required", out var required)
            || !required.TryGetProperty("ckpt_name", out var ckptName)
            || ckptName.ValueKind != JsonValueKind.Array
            || ckptName.GetArrayLength() == 0
            || ckptName[0].ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return ckptName[0].EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }

    private async Task<IReadOnlyList<string>?> LoadCachedAsync(CancellationToken ct)
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path)) return null;

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<string>>(stream, CacheJsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI checkpoint cache load failed");
            return null;
        }
    }

    private async Task SaveCachedAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = CachePath();
            var tempPath = path + ".tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, names, CacheJsonOptions, ct);
            }

            // File.Move(overwrite: true) is atomic on NTFS — same convention as ModelCatalogService.
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI checkpoint cache save failed");
        }
    }

    private string CachePath() => Path.Combine(_cacheDirectory, CacheFileName);
}
