using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Lists the user's ComfyUI workflow templates (API-format exports in
/// OutputPaths.ComfyWorkflowsDirectory) as model-picker entries. Local disk scan — no HTTP,
/// no token, never cached: the folder is rescanned on every catalog load/refresh so renamed
/// or deleted workflows can't go stale.
/// </summary>
public interface IComfyUiWorkflowCatalogService
{
    Task<IReadOnlyList<ModelOption>> FetchAsync(CancellationToken ct = default);
}
