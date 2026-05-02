using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Wraps IModelCatalogService + IModelDescriptorRegistry, so the VM doesn't have to know
/// about the disk cache, the live fetch, OR the seed-merge logic — just call Refresh / LoadCached
/// and bind the result. Both methods return the seeds-merged list (or null on failure / empty).
/// </summary>
public interface IModelCatalogCoordinator
{
    Task<IReadOnlyList<ModelOption>?> LoadCachedAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ModelOption>?> RefreshAsync(string apiToken, CancellationToken ct = default);
}
