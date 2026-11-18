using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class ModelCatalogCoordinator : IModelCatalogCoordinator
{
    private readonly IModelCatalogService _catalogService;
    private readonly IPollinationsCatalogService _pollinationsCatalogService;
    private readonly IComfyUiWorkflowCatalogService _comfyUiWorkflowCatalogService;
    private readonly IModelDescriptorRegistry _registry;

    public ModelCatalogCoordinator(
        IModelCatalogService catalogService,
        IPollinationsCatalogService pollinationsCatalogService,
        IComfyUiWorkflowCatalogService comfyUiWorkflowCatalogService,
        IModelDescriptorRegistry registry)
    {
        _catalogService = catalogService;
        _pollinationsCatalogService = pollinationsCatalogService;
        _comfyUiWorkflowCatalogService = comfyUiWorkflowCatalogService;
        _registry = registry;
    }

    public async Task<IReadOnlyList<ModelOption>?> LoadCachedAsync(CancellationToken ct = default)
    {
        // ComfyUI entries come from a live folder scan on EVERY load (never the disk cache):
        // they're free to enumerate and must reflect renames/deletes immediately — and they
        // should appear on first launch even when no remote catalog was ever cached.
        var cached = await _catalogService.LoadCachedAsync(ct);
        var comfy = await _comfyUiWorkflowCatalogService.FetchAsync(ct);

        var combined = (cached ?? []).Concat(comfy).ToList();
        if (combined.Count == 0) return null;
        return MergeWithSeeds(combined);
    }

    public async Task<IReadOnlyList<ModelOption>?> RefreshAsync(string apiToken, CancellationToken ct = default)
    {
        // Fan out to both remote providers in parallel — the Pollinations call is anonymous and
        // independent of the Replicate token, so it shouldn't be gated by Replicate's auth.
        // Tuple-await synchronises both already-running tasks without the .Result anti-pattern
        // that a separate WhenAll would otherwise leave behind.
        var replicateTask = _catalogService.FetchAsync(apiToken, ct);
        var pollinationsTask = _pollinationsCatalogService.FetchAsync(ct);
        var (replicate, pollinations) = (await replicateTask, await pollinationsTask);

        var fetched = replicate.Concat(pollinations).ToList();
        var comfy = await _comfyUiWorkflowCatalogService.FetchAsync(ct);
        if (fetched.Count == 0 && comfy.Count == 0) return null;

        // Cache the raw merged-fetched list (not seeds, not comfy — the workflow folder is
        // rescanned live every time, so caching it would only let stale entries linger).
        // Load-time merge keeps any freshly-added seed entries surfacing even when the cache
        // was written before they existed.
        if (fetched.Count > 0) await _catalogService.SaveCachedAsync(fetched, ct);
        return MergeWithSeeds([.. fetched, .. comfy]);
    }

    /// <summary>
    /// Always union the hardcoded fallback seeds into whatever came from disk/live so models
    /// we've explicitly added to the codebase still appear even when the cached catalog is
    /// stale or Replicate's curated collection hasn't picked them up yet. Incoming entries win
    /// on duplicates (their Display/Provider reflect live data).
    ///
    /// Live entries this build has no field list for are marked, and stay in the list: seeing a
    /// model is how its owner decides whether it is worth a descriptor, and an invisible model
    /// cannot be evaluated. Marked on the way out only — the disk cache keeps the provider's clean
    /// name, because a cached suffix would outlive its own truth: the build that finally adds flare's
    /// descriptor would still read "· not supported" off disk. The EndsWith guard keeps a reload
    /// from stacking a second suffix on the same entry.
    /// </summary>
    private IReadOnlyList<ModelOption> MergeWithSeeds(IReadOnlyList<ModelOption> live)
    {
        var seen = new HashSet<string>(live.Select(m => m.Value), StringComparer.OrdinalIgnoreCase);
        var marked = live.Select(m => MarkIfUnknown(m));
        return [.. marked, .. _registry.Seeds.Where(m => seen.Add(m.Value))];
    }

    // Same shape as PollinationsCatalogService.PaidSuffix: the picker binds ModelOption.Display
    // directly, so this is the whole mechanism behind "not supported".
    private const string UnknownModelSuffix = " · not supported";

    private ModelOption MarkIfUnknown(ModelOption option)
        => _registry.IsSchemaKnown(option.Value)
            || option.Display.EndsWith(UnknownModelSuffix, StringComparison.Ordinal) ? option
            : option with { Display = option.Display + UnknownModelSuffix };
}
