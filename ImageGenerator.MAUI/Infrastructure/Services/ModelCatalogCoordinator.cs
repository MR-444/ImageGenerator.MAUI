using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class ModelCatalogCoordinator : IModelCatalogCoordinator
{
    private readonly IModelCatalogService _catalogService;
    private readonly IPollinationsCatalogService _pollinationsCatalogService;
    private readonly IComfyUiWorkflowCatalogService _comfyUiWorkflowCatalogService;
    private readonly IModelDescriptorRegistry _registry;
    private readonly ILogger<ModelCatalogCoordinator> _logger;

    public ModelCatalogCoordinator(
        IModelCatalogService catalogService,
        IPollinationsCatalogService pollinationsCatalogService,
        IComfyUiWorkflowCatalogService comfyUiWorkflowCatalogService,
        IModelDescriptorRegistry registry,
        ILogger<ModelCatalogCoordinator> logger)
    {
        _catalogService = catalogService;
        _pollinationsCatalogService = pollinationsCatalogService;
        _comfyUiWorkflowCatalogService = comfyUiWorkflowCatalogService;
        _registry = registry;
        _logger = logger;
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
        return MergeWithSeeds((ModelOption[])[.. fetched, .. comfy]);
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
        LogDescriptorCoverage(live);
        var seen = new HashSet<string>(live.Select(m => m.Value), StringComparer.OrdinalIgnoreCase);
        var marked = live.Select(m => MarkIfUnknown(m));
        return (ModelOption[])[.. marked, .. _registry.Seeds.Where(m => seen.Add(m.Value))];
    }

    // Same shape as PollinationsCatalogService.PaidSuffix: the picker binds ModelOption.Display
    // directly, so this is the whole mechanism behind "not supported".
    private const string UnknownModelSuffix = " · not supported";

    private ModelOption MarkIfUnknown(ModelOption option)
        => _registry.IsSchemaKnown(option.Value)
            || option.Display.EndsWith(UnknownModelSuffix, StringComparison.Ordinal) ? option
            : option with { Display = option.Display + UnknownModelSuffix };

    /// <summary>
    /// Says out loud, in app.log, where the field lists this build ships and the catalog it was just given
    /// stop agreeing. The two lists have to agree and nothing else notices: a catalog entry spelled
    /// differently than its descriptor is reported to the user as a model this build cannot drive, and a
    /// descriptor the catalog stopped offering is either unreachable code or a model that no longer exists.
    /// Nothing here changes what the app does — it marks, refuses and generates exactly as before, because
    /// auto-matching a rename is how the next rename would come to look fine.
    ///
    /// Compared against <paramref name="live"/>, never the merged list: seeds are the descriptor ids
    /// themselves unioned back in, so matching against the merge would let every descriptor find its own
    /// seed entry and pass forever.
    /// </summary>
    private void LogDescriptorCoverage(IReadOnlyList<ModelOption> live)
    {
        var exact = new HashSet<string>(live.Select(m => m.Value), StringComparer.Ordinal);
        var anyCase = new HashSet<string>(live.Select(m => m.Value), StringComparer.OrdinalIgnoreCase);
        var seeded = new HashSet<string>(_registry.Seeds.Select(m => m.Value), StringComparer.Ordinal);

        foreach (var id in _registry.DescribedModelIds)
        {
            if (exact.Contains(id)) continue;

            // Same model, two spellings. Reported to the user as the opposite of the truth: the entry is
            // suffixed, the note claims there is no field list, and generating is refused for a model this
            // build can drive. That is worth a warning rather than a note.
            if (anyCase.Contains(id))
            {
                var catalogId = live
                    .First(m => string.Equals(m.Value, id, StringComparison.OrdinalIgnoreCase)).Value;
                _logger.LogWarning(
                    "The field list for {DescriptorId} does not line up with the catalog spelling of the same model ({CatalogId}): known-ness is case-sensitive, so a model this build can drive is marked unsupported",
                    id, catalogId);
                continue;
            }

            if (seeded.Contains(id))
            {
                // Benign by design — a seed entry is what makes a model added in code usable before the
                // curated list carries it. Not benign forever: if upstream retired the model, the seed keeps
                // it selectable and every generation against it fails at request time.
                _logger.LogInformation(
                    "The catalog has no entry for {DescriptorId}; the app still offers it from its own seed entry, so check the model still exists upstream",
                    id);
                continue;
            }

            _logger.LogWarning(
                "No catalog entry offers {DescriptorId} and it has no seed entry: this build ships a field list nobody can select",
                id);
        }
    }
}
