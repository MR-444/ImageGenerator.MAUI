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
}
