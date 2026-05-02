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

    /// <summary>The hardcoded seed list shown before Refresh Models hydrates the catalog.</summary>
    IReadOnlyList<ModelOption> Seeds { get; }
}
