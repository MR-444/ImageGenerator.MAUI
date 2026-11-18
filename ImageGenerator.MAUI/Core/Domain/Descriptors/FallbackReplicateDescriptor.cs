using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// Payload + capabilities for any "owner/name" model surfaced by Refresh Models that has no
/// dedicated descriptor — see IModelDescriptorRegistry.IsSchemaKnown, which is how the UI knows to
/// say so.
///
/// Deliberately the smallest request that works. This payload used to send six fields, and both
/// directions of that were wrong: <c>aspect_ratio</c> carried eleven hand-written ratios that no
/// model was asked about (an out-of-enum value on a declared field is exactly what cog REJECTS — the
/// 422 that started this), <c>output_format</c> sent <c>"jpg"</c> where the gpt-image family declares
/// <c>jpeg</c> (same failure, waiting for whoever picked JPG), and <c>seed</c>/<c>output_quality</c>
/// rode on names these models mostly do not declare, which cog now silently drops — a knob that
/// looks live and changes nothing.
///
/// What survives is what essentially every Replicate image model takes: the prompt, and the user's
/// attached image. Output format is NOT sent — the app re-encodes to the requested format on save,
/// so the field was a request the app didn't need to make. Aspect ratio is not offered at all in the
/// UI for these models rather than sent and rejected.
///
/// Not a full descriptor: no IMetadataDescriber (unknown extras to write) and no ICatalogSeedEntry
/// (only known models seed the picker on first launch). The registry holds it internally and uses it
/// as the fallback for PayloadFor / CapabilitiesFor lookups.
/// </summary>
public sealed class FallbackReplicateDescriptor : IPayloadBuilder, ICapabilityProvider
{
    // The UI hides the aspect-ratio picker for these models (AspectRatio: false), but
    // GeneratorViewModel.RefreshCapabilities indexes AspectRatios[0] before it consults that flag,
    // so the list must stay non-empty. Same reason Ideogram carries a 1:1 it never shows.
    private static readonly string[] NotShownButIndexed = ["1:1"];

    // Sentinel — the registry never indexes the fallback by id; it's invoked when a lookup misses.
    public string ModelId => "_fallback";

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
        // No AR, no seed, no quality: this payload sends none of them, so showing them would be a
        // lie. Output format stays selectable because the app transcodes on save — that choice is
        // honoured without asking the model.
        AspectRatio: false, CustomDimensions: false, Seed: false, ImagePrompt: true,
        AspectRatioLabel: "Aspect ratio", AspectRatios: NotShownButIndexed,
        MaxImageInputs: 1);

    public object Build(ImageGenerationParameters p)
    {
        var input = new Dictionary<string, object?>
        {
            ["prompt"] = p.Prompt
        };

        // Both spellings, because which one a model declares is precisely the thing that is not
        // known here: the OpenAI-family models use `input_images`, most others use `images`. Cog
        // drops the undeclared one, so the cost of sending both is a model that declares one of the
        // two names as something other than an array of URIs — a rarer bet than the naming split it
        // covers, and losing the user's image in silence is the worse failure of the two.
        var images = ImageDataUriEncoder.BuildDataUris(p.ImagePrompts, maxCount: 1);
        if (images is not null)
        {
            input["images"] = images;
            input["input_images"] = images;
        }

        return input;
    }
}
