using System.Text.Json;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class ModelCatalogService : IModelCatalogService
{
    private const string CacheFileName = "model-catalog.json";
    private static readonly HashSet<string> ReplicateOwnerAllowlist =
        new(StringComparer.OrdinalIgnoreCase) { "black-forest-labs", "openai", "google" };

    // Single options instance for both save and load — keeps the disk format pinned so
    // adding `Web` defaults or a converter on one path can't silently desync the other.
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReplicateApi _replicateApi;
    private readonly string _cacheDirectory;
    private readonly ILogger<ModelCatalogService> _logger;

    public ModelCatalogService(
        IReplicateApi replicateApi,
        ILogger<ModelCatalogService> logger,
        string? cacheDirectoryOverride = null)
    {
        _replicateApi = replicateApi ?? throw new ArgumentNullException(nameof(replicateApi));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Tests override this with a temp path; production resolves the MAUI app-data dir.
        _cacheDirectory = cacheDirectoryOverride ?? FileSystem.AppDataDirectory;
    }

    public async Task<IReadOnlyList<ModelOption>> FetchAsync(string apiToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiToken)) return [];

        var bearer = $"Bearer {apiToken}";
        return await SafeFetchReplicateAsync(bearer, ct);
    }

    private async Task<IReadOnlyList<ModelOption>> SafeFetchReplicateAsync(string bearer, CancellationToken ct)
    {
        try
        {
            var coll = await _replicateApi.GetTextToImageCollectionAsync(bearer, ct);
            // Scope the catalog to the supported owners only. The curated text-to-image
            // collection hosts many other owners (stability-ai, bytedance, etc.) that this app
            // doesn't support — including them clutters the picker. Every surviving owner is
            // grouped under the single "Replicate" provider (the creator shows in the name).
            return coll.Models
                .Where(m => !string.IsNullOrWhiteSpace(m.Owner)
                            && !string.IsNullOrWhiteSpace(m.Name)
                            && ReplicateOwnerAllowlist.Contains(m.Owner!))
                .Select(m => new ModelOption(m.Name!, $"{m.Owner}/{m.Name}", ProviderConstants.Replicate))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replicate catalog fetch failed");
            return [];
        }
    }

    public async Task<IReadOnlyList<ModelOption>?> LoadCachedAsync(CancellationToken ct = default)
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path)) return null;

            await using var stream = File.OpenRead(path);
            var models = await JsonSerializer.DeserializeAsync<List<ModelOption>>(stream, CacheJsonOptions, ct);
            return models;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model catalog cache load failed");
            return null;
        }
    }

    public async Task SaveCachedAsync(IReadOnlyList<ModelOption> models, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = CachePath();
            var tempPath = path + ".tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, models, CacheJsonOptions, ct);
            }

            // File.Move(overwrite: true) is atomic on NTFS and avoids File.Replace's
            // "destination held open" failure mode.
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model catalog cache save failed");
        }
    }

    private string CachePath() => Path.Combine(_cacheDirectory, CacheFileName);
}
