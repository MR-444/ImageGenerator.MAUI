using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// Conservative payload + capability set for any owner/name model surfaced by Refresh Models
/// that doesn't have a dedicated descriptor. May still 422 on models with non-standard field
/// names — that's the signal to add a real descriptor.
///
/// Not a full descriptor: no IMetadataDescriber (unknown extras to write) and no
/// ICatalogSeedEntry (only known models seed the picker on first launch). The registry holds
/// it internally and uses it as the fallback for PayloadFor / CapabilitiesFor lookups.
/// </summary>
public sealed class FallbackReplicateDescriptor : IPayloadBuilder, ICapabilityProvider
{
    private static readonly string[] AspectRatios =
        ["1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4"];

    // Sentinel — the registry never indexes the fallback by id; it's invoked when a lookup misses.
    public string ModelId => "_fallback";

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
        AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        MaxImageInputs: 1);

    public object Build(ImageGenerationParameters p) => new Dictionary<string, object?>
    {
        ["prompt"] = p.Prompt,
        ["seed"] = p.Seed,
        ["aspect_ratio"] = p.AspectRatio,
        ["output_format"] = p.OutputFormat.ToString().ToLowerInvariant(),
        ["output_quality"] = p.OutputQuality,
        ["images"] = ImageDataUriEncoder.BuildDataUris(p.ImagePrompts, maxCount: 1)
    };
}
