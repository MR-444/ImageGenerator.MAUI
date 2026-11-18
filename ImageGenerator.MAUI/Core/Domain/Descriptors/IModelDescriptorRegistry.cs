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
    /// one wire API; every <c>comfyui/*</c> id is the user's own workflow file). False means the
    /// lookups above answer with the conservative Replicate fallback — the request is a guess, so
    /// the UI says so instead of showing knobs whose fields that guess may not include.
    ///
    /// This reports the absence of knowledge; it never changes what a lookup returns. Making
    /// PayloadFor throw on an unknown id would turn "usually works" into "never works".
    /// </summary>
    bool IsSchemaKnown(string modelId);

    /// <summary>The hardcoded seed list shown before Refresh Models hydrates the catalog.</summary>
    IReadOnlyList<ModelOption> Seeds { get; }
}
