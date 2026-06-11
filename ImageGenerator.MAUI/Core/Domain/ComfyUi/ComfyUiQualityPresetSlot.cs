namespace ImageGenerator.MAUI.Core.Domain.ComfyUi;

/// <summary>
/// The quality-preset slot of a workflow template: the choice baked into the export plus the
/// node's own option labels (literal non-empty option1..option4, in slot order). Qualifies
/// only when the workflow contains EXACTLY ONE CustomCombo node with a literal choice — the
/// class is generic enough to drive anything, so an ambiguous graph exposes no slot (same
/// convention as the multi-UNET rule on <see cref="ComfyUiModelSlot"/>). Deliberately a
/// separate record: a workflow can carry both a model slot and a preset slot (the Ideogram4
/// sample does), and the two pickers show independently.
/// </summary>
public sealed record ComfyUiQualityPresetSlot(string BakedChoice, IReadOnlyList<string> Options);
