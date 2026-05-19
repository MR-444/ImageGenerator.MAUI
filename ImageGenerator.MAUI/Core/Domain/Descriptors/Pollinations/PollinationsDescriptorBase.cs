using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Pollinations;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.Pollinations;

/// <summary>
/// Shared base for the Pollinations descriptors. The three current models (flux / turbo /
/// stable-diffusion) take identical parameters at the API level; only the model-name string
/// and display label differ. Subclasses provide those.
/// </summary>
public abstract class PollinationsDescriptorBase : IPayloadBuilder, ICapabilityProvider, IMetadataDescriber, ICatalogSeedEntry
{
    // Curated AR set with clean integer translations to width/height. "custom" falls through
    // to parameters.Width/Height untouched. Pollinations accepts arbitrary pixel sizes, so
    // this list is purely a UX convenience — not a server constraint.
    private static readonly string[] AspectRatios =
        ["1:1", "16:9", "9:16", "4:3", "3:4", "custom"];

    private static readonly Dictionary<string, (int W, int H)> AspectMap = new(StringComparer.Ordinal)
    {
        ["1:1"] = (1024, 1024),
        ["16:9"] = (1024, 576),
        ["9:16"] = (576, 1024),
        ["4:3"] = (1024, 768),
        ["3:4"] = (768, 1024),
    };

    private readonly string _displayName;
    private readonly string _serverModelName;

    protected PollinationsDescriptorBase(string displayName, string modelId, string serverModelName)
    {
        _displayName = displayName;
        ModelId = modelId;
        _serverModelName = serverModelName;
    }

    public string ModelId { get; }

    public ModelOption Seed => new(_displayName, ModelId, ProviderConstants.Pollinations);

    // Hook for the fallback descriptor: when a Pollinations model id isn't in the seed list
    // (e.g. catalog returned "pollinations/gptimage" but no seed was hardcoded for it),
    // the registry's fallback descriptor overrides this to derive the server name from p.Model
    // at request time instead of from the ctor-supplied constant.
    protected virtual string ResolveServerModelName(ImageGenerationParameters p) => _serverModelName;

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
        AspectRatio: true, CustomDimensions: true, Seed: true, ImagePrompt: false,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        Safe: true);
    // OutputFormatSelectable defaults to true: Pollinations returns JPEG over the wire, but
    // ImageFileService re-encodes to the user's chosen OutputFormat. PNG (the app default) is
    // re-encoded losslessly from the JPEG pixels; JPEG-to-JPEG at max quality is near-lossless.

    public object Build(ImageGenerationParameters p)
    {
        var (w, h) = ResolveDimensions(p);
        return new PollinationsRequest(
            Prompt: p.Prompt,
            Model: ResolveServerModelName(p),
            Width: w,
            Height: h,
            Seed: p.Seed,
            Safe: p.Safe);
    }

    public IEnumerable<string> Lines(ImageGenerationParameters p) =>
        [$"Safe: {p.Safe}"];

    private static (int W, int H) ResolveDimensions(ImageGenerationParameters p) =>
        AspectMap.TryGetValue(p.AspectRatio, out var preset)
            ? preset
            : (p.Width, p.Height);
}
