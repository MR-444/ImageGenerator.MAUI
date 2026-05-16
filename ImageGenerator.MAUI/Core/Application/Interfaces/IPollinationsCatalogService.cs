using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Fetches the live image-model list from Pollinations.ai. Distinct from IModelCatalogService
/// because the endpoint is unauthenticated (no token plumbing needed) and the response shape
/// is a plain string array rather than the Replicate collection JSON.
/// </summary>
public interface IPollinationsCatalogService
{
    Task<IReadOnlyList<ModelOption>> FetchAsync(CancellationToken ct = default);
}
