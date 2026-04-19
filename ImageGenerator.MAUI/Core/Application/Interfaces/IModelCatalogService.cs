using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Fetches the live model catalogs from Replicate (Flux family) and OpenAI (image models)
/// so the UI dropdown stays current instead of relying on a hardcoded list.
/// </summary>
public interface IModelCatalogService
{
    Task<IReadOnlyList<ModelOption>> FetchAsync(string apiToken, CancellationToken ct = default);

    Task<IReadOnlyList<ModelOption>?> LoadCachedAsync(CancellationToken ct = default);

    Task SaveCachedAsync(IReadOnlyList<ModelOption> models, CancellationToken ct = default);
}
