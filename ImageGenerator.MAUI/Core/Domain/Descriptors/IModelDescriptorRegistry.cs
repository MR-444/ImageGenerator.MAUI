using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// Single lookup point for per-model behavior. Every consumer (factory, capabilities, file
/// metadata, VM seed list) goes through this instead of switching on the model id directly.
/// </summary>
public interface IModelDescriptorRegistry
{
    /// <summary>Falls back to a conservative Replicate-shaped descriptor for unknown models.</summary>
    IPayloadBuilder PayloadFor(string modelId);

    /// <summary>Falls back to the conservative-capabilities descriptor for unknown models.</summary>
    ICapabilityProvider CapabilitiesFor(string modelId);

    /// <summary>Returns null for unknown models — callers append no model-specific metadata in that case.</summary>
    IMetadataDescriber? MetadataFor(string modelId);

    /// <summary>
    /// Whether this app build knows the model's field set: a dedicated descriptor exists, or the
    /// provider's API is uniform enough that none is needed (every <c>pollinations/*</c> id rides
    /// one wire API; every <c>comfyui/*</c> id is the user's own workflow file).
    ///
    /// False is a permission and not only a label. ReplicateImageGenerationService will not call such
    /// a model — no request is built, so nothing is billed — and the picker marks the entry rather
    /// than offering knobs for fields nobody has checked. <see cref="CapabilitiesFor"/> still answers,
    /// with the narrow set that keeps those pickers honest; <see cref="PayloadFor"/> throws.
    /// </summary>
    bool IsSchemaKnown(string modelId);

    /// <summary>The hardcoded seed list shown before Refresh Models hydrates the catalog.</summary>
    IReadOnlyList<ModelOption> Seeds { get; }

    /// <summary>
    /// The model ids this build ships a field list for — the same keys <see cref="PayloadFor"/> and
    /// <see cref="IsSchemaKnown"/> consult.
    ///
    /// It deliberately excludes the three fallbacks: they answer for models nobody described, so counting
    /// them here would make every "a descriptor no catalog entry offers" check permanently, unfalsifiably
    /// true.
    /// </summary>
    IReadOnlyCollection<string> DescribedModelIds { get; }
}
