using ImageGenerator.MAUI.Core.Domain.ComfyUi;

namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Probes workflow template files for the slots the generator UI surfaces: the baked-in
/// model (read-only display) and the CustomCombo quality preset.
/// </summary>
public interface IComfyUiCheckpointService
{
    /// <summary>
    /// The workflow template's baked model slot (loader kind + baked file name), or null
    /// when the workflow has none — the UI hides the model line then. See
    /// <see cref="ComfyUiModelSlot"/> for the exactly-one-UNETLoader rule.
    /// </summary>
    Task<ComfyUiModelSlot?> GetWorkflowModelSlotAsync(string workflowName, CancellationToken ct = default);

    /// <summary>
    /// The workflow template's quality-preset slot (baked choice + option labels), or null
    /// when the workflow has none — the UI hides the preset picker then. Options come from
    /// the file itself (no server fetch). See <see cref="ComfyUiQualityPresetSlot"/> for the
    /// exactly-one-CustomCombo rule.
    /// </summary>
    Task<ComfyUiQualityPresetSlot?> GetWorkflowQualityPresetSlotAsync(string workflowName, CancellationToken ct = default);

    /// <summary>
    /// True when the workflow template contains a LoadImage node — it consumes an input image
    /// (img2img / upscale), so the UI shows the Input Image card for it. False on missing or
    /// unreadable files.
    /// </summary>
    Task<bool> GetWorkflowHasInputImageAsync(string workflowName, CancellationToken ct = default);

    /// <summary>
    /// The workflow designated for the "Upscale after render" chain: the alphabetically first
    /// LoadImage-bearing workflow whose file stem contains "upscale" (case-insensitive).
    /// Null when none exists — the checkbox is hidden then.
    /// </summary>
    Task<string?> FindUpscaleWorkflowNameAsync(CancellationToken ct = default);
}
