using ImageGenerator.MAUI.Core.Domain.ComfyUi;

namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Surfaces the ComfyUI server's installed model files for the generator's model picker —
/// checkpoints (CheckpointLoaderSimple) and diffusion models (UNETLoader).
/// </summary>
public interface IComfyUiCheckpointService
{
    /// <summary>
    /// Live GET /object_info/&lt;loader-class&gt; against the configured server; on success the
    /// kind's disk cache is rewritten. Any failure (offline, malformed) falls back to that
    /// cached list; null when neither is available.
    /// </summary>
    Task<IReadOnlyList<string>?> GetModelNamesAsync(ComfyUiLoaderKind kind, CancellationToken ct = default);

    /// <summary>
    /// The workflow template's swappable model slot (loader kind + baked file name), or null
    /// when the workflow has none — the UI hides the picker then. See
    /// <see cref="ComfyUiModelSlot"/> for the exactly-one-UNETLoader rule.
    /// </summary>
    Task<ComfyUiModelSlot?> GetWorkflowModelSlotAsync(string workflowName, CancellationToken ct = default);
}
