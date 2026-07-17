namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;

/// <summary>
/// Typed payload the ComfyUI descriptor builds and the generation service consumes.
/// <para>
/// <see cref="WorkflowName"/> is the file stem of an API-format workflow export in
/// OutputPaths.ComfyWorkflowsDirectory. <see cref="AspectRatio"/> must be one of ComfyUI's
/// ResolutionSelector combo strings (patched verbatim); <see cref="Megapixels"/> is that
/// node's quality knob (1.0 ≈ 1024×1024). Null AR/MP means "leave the workflow's own value",
/// as does a null <see cref="PresetChoice"/> (the CustomCombo keeps its baked-in choice).
/// </para>
/// </summary>
public sealed record ComfyUiRequest(
    string WorkflowName,
    string Prompt,
    bool UseJsonPrompt,
    long Seed,
    string? AspectRatio,
    double? Megapixels,
    string? PresetChoice = null);
