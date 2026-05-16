using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class ModelCatalogCoordinator : IModelCatalogCoordinator
{
    private readonly IModelCatalogService _catalogService;
    private readonly IPollinationsCatalogService _pollinationsCatalogService;
    private readonly IModelDescriptorRegistry _registry;

    public ModelCatalogCoordinator(
        IModelCatalogService catalogService,
        IPollinationsCatalogService pollinationsCatalogService,
        IModelDescriptorRegistry registry)
    {
        _catalogService = catalogService;
        _pollinationsCatalogService = pollinationsCatalogService;
        _registry = registry;
    }

    public async Task<IReadOnlyList<ModelOption>?> LoadCachedAsync(CancellationToken ct = default)
    {
        var cached = await _catalogService.LoadCachedAsync(ct);
        if (cached is null or { Count: 0 }) return null;
        return MergeWithSeeds(cached);
    }

    public async Task<IReadOnlyList<ModelOption>?> RefreshAsync(string apiToken)
    {
        // Fan out to both providers in parallel — the Pollinations call is anonymous and
        // independent of the Replicate token, so it shouldn't be gated by Replicate's auth.
        var replicateTask = _catalogService.FetchAsync(apiToken);
        var pollinationsTask = _pollinationsCatalogService.FetchAsync();
        await Task.WhenAll(replicateTask, pollinationsTask);

        var fetched = replicateTask.Result.Concat(pollinationsTask.Result).ToList();
        if (fetched.Count == 0) return null;

        // Cache the raw merged-fetched list (not seeds) — load-time merge keeps any
        // freshly-added seed entries surfacing even when the cache was written before they existed.
        await _catalogService.SaveCachedAsync(fetched);
        return MergeWithSeeds(fetched);
    }

    /// <summary>
    /// Always union the hardcoded fallback seeds into whatever came from disk/live so models
    /// we've explicitly added to the codebase still appear even when the cached catalog is
    /// stale or Replicate's curated collection hasn't picked them up yet. Incoming entries win
    /// on duplicates (their Display/Provider reflect live data).
    /// </summary>
    private IReadOnlyList<ModelOption> MergeWithSeeds(IReadOnlyList<ModelOption> live)
    {
        var seen = new HashSet<string>(live.Select(m => m.Value), StringComparer.OrdinalIgnoreCase);
        return live.Concat(_registry.Seeds.Where(m => seen.Add(m.Value))).ToList();
    }
}
