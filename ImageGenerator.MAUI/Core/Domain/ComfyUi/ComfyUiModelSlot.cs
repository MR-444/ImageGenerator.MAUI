namespace ImageGenerator.MAUI.Core.Domain.ComfyUi;

/// <summary>Which loader node class holds a workflow's baked model file.</summary>
public enum ComfyUiLoaderKind
{
    /// <summary>CheckpointLoaderSimple — all-in-one checkpoint (model + CLIP + VAE).</summary>
    Checkpoint,

    /// <summary>UNETLoader — split-loader diffusion model from models/diffusion_models.</summary>
    Unet
}

/// <summary>
/// The model a workflow template bakes in — shown read-only in the UI (the workflow file IS
/// the model choice, so nothing is ever patched here). CheckpointLoaderSimple always wins;
/// UNETLoader only qualifies when EXACTLY ONE literal loader exists — multi-UNET graphs
/// (e.g. a conditional + unconditional pair feeding a DualModelGuider) have no single model
/// to name, so they expose no slot at all.
/// </summary>
public sealed record ComfyUiModelSlot(ComfyUiLoaderKind Kind, string BakedName);
