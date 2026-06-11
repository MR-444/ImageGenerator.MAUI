using System.Globalization;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.ComfyUi;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.ComfyUi;

/// <summary>
/// Handles every "comfyui/*" model id. ComfyUI "models" are the user's own workflow
/// templates (API-format exports scanned from disk), so there are no per-model descriptors
/// and no seed entries — one fallback covers them all, mirroring FallbackPollinationsDescriptor.
/// Not registered in DI; the registry holds it internally.
/// </summary>
public sealed class FallbackComfyUiDescriptor : IPayloadBuilder, ICapabilityProvider, IMetadataDescriber
{
    // ComfyUI's built-in ResolutionSelector node (comfy_extras.nodes_resolution) — these are
    // its aspect_ratio combo values VERBATIM; the workflow patcher writes the selected string
    // straight into that node, so the list must not be reworded.
    private static readonly string[] ResolutionSelectorAspectRatios =
    [
        "1:1 (Square)", "3:2 (Photo)", "4:3 (Standard)", "16:9 (Widescreen)",
        "21:9 (Ultrawide)", "2:3 (Portrait Photo)", "3:4 (Portrait Standard)",
        "9:16 (Portrait Widescreen)"
    ];

    // Megapixel presets for the same node's quality knob (FLOAT 0.1-16; 1.0 ≈ 1024×1024).
    // Surfaced through the shared resolution picker; Build parses the leading number.
    // "1.0 MP" leads so first-time selection defaults sensibly (RefreshCapabilities falls
    // back to options[0] — same default-first convention as Ideogram's AutoResolution).
    private static readonly string[] MegapixelOptions =
        ["1.0 MP", "0.5 MP", "1.5 MP", "2.0 MP", "3.0 MP", "4.0 MP"];

    public string ModelId => "_fallback_comfyui";

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
        AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: false,
        AspectRatioLabel: "Aspect ratio", AspectRatios: ResolutionSelectorAspectRatios,
        Resolutions: MegapixelOptions,
        JsonPromptEditor: true);

    public object Build(ImageGenerationParameters p) => new ComfyUiRequest(
        WorkflowName: ModelConstants.ComfyUi.WorkflowName(p.Model),
        Prompt: p.Prompt,
        UseJsonPrompt: p.UseJsonPrompt,
        Seed: p.Seed,
        AspectRatio: ResolutionSelectorAspectRatios.Contains(p.AspectRatio, StringComparer.Ordinal)
            ? p.AspectRatio
            : null,
        Megapixels: ParseMegapixels(p.Resolution),
        CheckpointName: string.IsNullOrWhiteSpace(p.ComfyUiCheckpoint) ? null : p.ComfyUiCheckpoint);

    public IEnumerable<string> Lines(ImageGenerationParameters p)
    {
        yield return $"Workflow: {ModelConstants.ComfyUi.WorkflowName(p.Model)}";
        yield return $"JsonPrompt: {p.UseJsonPrompt}";
        if (ParseMegapixels(p.Resolution) is { } mp)
            yield return $"Megapixels: {mp.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(p.ComfyUiCheckpoint))
            yield return $"Checkpoint: {p.ComfyUiCheckpoint}";
    }

    /// <summary>"1.5 MP" → 1.5; anything unparseable → null (the workflow keeps its own value).</summary>
    internal static double? ParseMegapixels(string? resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution)) return null;
        var numberPart = resolution.Split(' ')[0];
        return double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var mp)
            ? mp
            : null;
    }
}
