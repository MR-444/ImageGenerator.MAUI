using System.Text.Json;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

/// <summary>
/// Fetches the server's installed model files via the narrow GET /object_info/&lt;class&gt;
/// endpoints (a few KB each) instead of the full /object_info dump (multi-MB on large
/// installs). Successful fetches rewrite a per-kind disk cache so the picker stays populated
/// while the server is offline; cache conventions mirror ModelCatalogService.
/// </summary>
public sealed class ComfyUiCheckpointService : IComfyUiCheckpointService
{
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

    /// <summary>One row per loader kind: where to fetch, what to drill, where to cache.</summary>
    private static (string ClassName, string InputKey, string CacheFile) Map(ComfyUiLoaderKind kind) => kind switch
    {
        ComfyUiLoaderKind.Checkpoint => ("CheckpointLoaderSimple", "ckpt_name", "comfyui-checkpoints.json"),
        ComfyUiLoaderKind.Unet => ("UNETLoader", "unet_name", "comfyui-unets.json"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public async Task<IReadOnlyList<string>?> GetModelNamesAsync(ComfyUiLoaderKind kind, CancellationToken ct = default)
    {
        var (className, inputKey, cacheFile) = Map(kind);

        var baseUrl = _uiStateStore.LoadComfyUiBaseUrl() ?? ModelConstants.ComfyUi.DefaultBaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            _logger.LogWarning("ComfyUI model-list fetch skipped: invalid base URL '{BaseUrl}'", baseUrl);
            return await LoadCachedAsync(cacheFile, ct);
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient(ComfyUiImageGenerationService.HttpClientName);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(FetchTimeout);

            using var response = await httpClient.GetAsync(
                new Uri(baseUri, "object_info/" + className), timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);

            var names = ParseModelNames(doc, className, inputKey);
            if (names is null)
            {
                _logger.LogWarning("ComfyUI /object_info/{ClassName} had an unexpected shape", className);
                return await LoadCachedAsync(cacheFile, ct);
            }

            _logger.LogInformation("ComfyUI model list fetched Kind={Kind} Count={Count}", kind, names.Count);
            await SaveCachedAsync(names, cacheFile, ct);
            return names;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI model-list fetch failed Kind={Kind}, falling back to cache", kind);
            return await LoadCachedAsync(cacheFile, ct);
        }
    }

    public async Task<ComfyUiModelSlot?> GetWorkflowModelSlotAsync(string workflowName, CancellationToken ct = default)
    {
        try
        {
            var path = Path.Combine(_workflowsDirectory, workflowName + ".json");
            if (!File.Exists(path)) return null;

            var template = await File.ReadAllTextAsync(path, ct);
            return ComfyUiWorkflowPatcher.FindBakedModelSlot(template);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI workflow model-slot probe failed Workflow={Workflow}", workflowName);
            return null;
        }
    }

    /// <summary>Drills &lt;className&gt; → input → required → &lt;inputKey&gt; → [0]; null on any mismatch.</summary>
    private static List<string>? ParseModelNames(JsonDocument doc, string className, string inputKey)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty(className, out var cls)
            || !cls.TryGetProperty("input", out var input)
            || !input.TryGetProperty("required", out var required)
            || !required.TryGetProperty(inputKey, out var nameInput)
            || nameInput.ValueKind != JsonValueKind.Array
            || nameInput.GetArrayLength() == 0
            || nameInput[0].ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return nameInput[0].EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }

    private async Task<IReadOnlyList<string>?> LoadCachedAsync(string cacheFile, CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(_cacheDirectory, cacheFile);
            if (!File.Exists(path)) return null;

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<string>>(stream, CacheJsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI model-list cache load failed File={File}", cacheFile);
            return null;
        }
    }

    private async Task SaveCachedAsync(IReadOnlyList<string> names, string cacheFile, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = Path.Combine(_cacheDirectory, cacheFile);
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
            _logger.LogWarning(ex, "ComfyUI model-list cache save failed File={File}", cacheFile);
        }
    }
}
