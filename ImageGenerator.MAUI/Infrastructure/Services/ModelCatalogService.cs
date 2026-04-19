using System.Diagnostics;
using System.Text.Json;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public class ModelCatalogService : IModelCatalogService
{
    private const string CacheFileName = "model-catalog.json";
    private static readonly HashSet<string> ReplicateOwnerAllowlist =
        new(StringComparer.OrdinalIgnoreCase) { "black-forest-labs", "openai", "google" };

    // Single options instance for both save and load — keeps the disk format pinned so
    // adding `Web` defaults or a converter on one path can't silently desync the other.
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReplicateApi _replicateApi;
    private readonly string _cacheDirectory;

    public ModelCatalogService(
        IReplicateApi replicateApi,
        string? cacheDirectoryOverride = null)
    {
        _replicateApi = replicateApi ?? throw new ArgumentNullException(nameof(replicateApi));
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
            // Scope the catalog to Flux + OpenAI owners only. The curated text-to-image
            // collection hosts many other owners (stability-ai, google, bytedance, etc.)
            // that this app doesn't support — including them clutters the picker.
            return coll.Models
                .Where(m => !string.IsNullOrWhiteSpace(m.Owner)
                            && !string.IsNullOrWhiteSpace(m.Name)
                            && ReplicateOwnerAllowlist.Contains(m.Owner!))
                .Select(m => new ModelOption(m.Name!, $"{m.Owner}/{m.Name}", FormatProvider(m.Owner!)))
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Replicate catalog fetch failed: {ex.Message}");
            return [];
        }
    }

    private static string FormatProvider(string owner) => owner switch
    {
        "black-forest-labs" => "Black Forest Labs",
        "openai" => "OpenAI (via Replicate)",
        "google" => "Google",
        // The allowlist guarantees `owner` is one of the above. Throw if a new entry is ever
        // added without a matching display label — silent fallthrough would ship the raw slug.
        _ => throw new InvalidOperationException($"Unexpected owner past allowlist: {owner}")
    };

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
            Debug.WriteLine($"Model catalog cache load failed: {ex.Message}");
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
            Debug.WriteLine($"Model catalog cache save failed: {ex.Message}");
        }
    }

    private string CachePath() => Path.Combine(_cacheDirectory, CacheFileName);
}
