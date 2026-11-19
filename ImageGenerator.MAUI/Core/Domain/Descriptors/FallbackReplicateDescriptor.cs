using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// What this build can say about an "owner/name" model surfaced by Refresh Models that has no
/// dedicated descriptor — see IModelDescriptorRegistry.IsSchemaKnown, which is what the UI asks.
///
/// Capabilities only. There used to be a <c>Build</c> here producing the smallest request that
/// worked — the prompt, plus the user's image — and Replicate answered it at the model's own
/// defaults, which is how picking 9:16 produced a square image while the saved file's metadata went
/// on insisting the aspect ratio had been 9:16. Not knowing a model's fields is not a reason to send
/// it a request, so ReplicateImageGenerationService now refuses such models before anything leaves
/// the machine, and the payload half of this class went with them rather than sitting unreachable.
///
/// What is left is what the pickers need in order to stay honest about an unknown model: no aspect
/// ratio, no seed, no quality — none of which could have been sent anyway — and output format still
/// selectable, because the app transcodes on save and that choice never asks the model anything.
///
/// Not a full descriptor: no IPayloadBuilder, no IMetadataDescriber (no idea what to write), no
/// ICatalogSeedEntry (only known models seed the picker on first launch).
/// </summary>
public sealed class FallbackReplicateDescriptor : ICapabilityProvider
{
    // Never shown: AspectRatio is false below, which hides the whole aspect-ratio block.
    // It still has to be a plausible list, because the aspect-ratio Picker is two-way bound straight
    // to Parameters.AspectRatio and swapping ItemsSource pushes null into that source whenever the
    // current value is missing from the new list — and RefreshCapabilities reads AspectRatios[0]
    // before that flag is consulted. Broad rather than one dummy value so that switching TO an
    // unknown model leaves the user's saved ratio alone: nothing is sent to such a model, so this
    // class has no opinion worth persisting.
    private static readonly string[] AspectRatios =
        ["1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4"];

    // Sentinel — the registry never indexes the fallback by id; it's invoked when a lookup misses.
    public string ModelId => "_fallback";

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
        // No AR, no seed, no quality, no reference image: nothing goes to this model at all, so
        // showing any of them would be a lie — and an expensive-looking one, since accepting a
        // reference image is how a user comes to expect an edit. Output format stays selectable
        // because the app transcodes on save; that choice is honoured without asking the model.
        AspectRatio: false, CustomDimensions: false, Seed: false, ImagePrompt: false,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        MaxImageInputs: 0);
}
