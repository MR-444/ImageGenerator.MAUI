namespace ImageGenerator.MAUI.Core.Domain.ComfyUi;

/// <summary>Which loader node class holds a workflow's swappable model file.</summary>
public enum ComfyUiLoaderKind
{
    /// <summary>CheckpointLoaderSimple — all-in-one checkpoint (model + CLIP + VAE).</summary>
    Checkpoint,

    /// <summary>UNETLoader — split-loader diffusion model from models/diffusion_models.</summary>
    Unet
}

/// <summary>
/// The single swappable model slot of a workflow template: the loader kind plus the file name
/// baked into the export. CheckpointLoaderSimple always wins; UNETLoader only qualifies when
/// EXACTLY ONE literal loader exists — multi-UNET graphs (e.g. a conditional + unconditional
/// pair feeding a DualModelGuider) are deliberate pairings that must never be half-swapped,
/// so they expose no slot at all.
/// </summary>
public sealed record ComfyUiModelSlot(ComfyUiLoaderKind Kind, string BakedName);
