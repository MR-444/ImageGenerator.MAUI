namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Surfaces the ComfyUI server's installed checkpoints for the generator's checkpoint picker.
/// </summary>
public interface IComfyUiCheckpointService
{
    /// <summary>
    /// Live GET /object_info/CheckpointLoaderSimple against the configured server; on success
    /// the disk cache is rewritten. Any failure (offline, malformed) falls back to the cached
    /// list; null when neither is available.
    /// </summary>
    Task<IReadOnlyList<string>?> GetCheckpointsAsync(CancellationToken ct = default);

    /// <summary>
    /// The workflow template's baked-in literal ckpt_name (lowest-id CheckpointLoaderSimple
    /// node), or null when the workflow has none — the UI hides the picker then.
    /// </summary>
    Task<string?> GetWorkflowCheckpointAsync(string workflowName, CancellationToken ct = default);
}
