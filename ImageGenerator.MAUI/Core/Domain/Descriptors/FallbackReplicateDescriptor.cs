using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// What this build can say about an "owner/name" model surfaced by Refresh Models that has no
/// dedicated descriptor — see IModelDescriptorRegistry.IsSchemaKnown, which is what the UI asks.
///
/// Capabilities only: ReplicateImageGenerationService refuses models without a descriptor before
/// anything leaves the machine, because Replicate would answer a bare request at the model's own
/// defaults and ignore the picked settings.
///
/// The capabilities are what the pickers need in order to stay honest about an unknown model: no aspect
/// ratio, no seed, no quality, no reference image, no output format.
///
/// Not a full descriptor: no IPayloadBuilder, no IMetadataDescriber (no idea what to write), no
/// ICatalogSeedEntry (only known models seed the picker on first launch).
/// </summary>
public sealed class FallbackReplicateDescriptor : ICapabilityProvider
{
    // Sentinel — the registry never indexes the fallback by id; it's invoked when a lookup misses.
    public string ModelId => "_fallback";

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
        // No AR, no seed, no quality, no reference image, no output format: nothing goes to this
        // model at all, so showing any of them would be a lie — and an expensive-looking one, since
        // accepting a reference image is how a user comes to expect an edit.
        AspectRatio: false, CustomDimensions: false, Seed: false, ImagePrompt: false,
        // No ratios either: the generator keeps the current ratio for a model that offers none.
        AspectRatioLabel: "Aspect ratio", AspectRatios: [],
        MaxImageInputs: 0, OutputFormatSelectable: false);
}
