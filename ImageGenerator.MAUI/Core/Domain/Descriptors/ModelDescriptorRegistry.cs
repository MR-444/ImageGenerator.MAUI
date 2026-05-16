using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Pollinations;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

public sealed class ModelDescriptorRegistry : IModelDescriptorRegistry
{
    private readonly Dictionary<string, IPayloadBuilder> _payloads;
    private readonly Dictionary<string, ICapabilityProvider> _capabilities;
    private readonly Dictionary<string, IMetadataDescriber> _metadata;
    private readonly FallbackReplicateDescriptor _replicateFallback;
    private readonly FallbackPollinationsDescriptor _pollinationsFallback;

    public ModelDescriptorRegistry(
        IEnumerable<IPayloadBuilder> payloads,
        IEnumerable<ICapabilityProvider> capabilities,
        IEnumerable<IMetadataDescriber> metadata,
        IEnumerable<ICatalogSeedEntry> seeds)
    {
        _payloads = payloads.ToDictionary(p => p.ModelId, StringComparer.Ordinal);
        _capabilities = capabilities.ToDictionary(c => c.ModelId, StringComparer.Ordinal);
        _metadata = metadata.ToDictionary(m => m.ModelId, StringComparer.Ordinal);
        Seeds = seeds.Select(s => s.Seed).ToList();
        // Held internally rather than DI-registered so neither fallback ever appears in the
        // IEnumerables above (and neither contributes a seed entry to the picker).
        _replicateFallback = new FallbackReplicateDescriptor();
        _pollinationsFallback = new FallbackPollinationsDescriptor();
    }

    public IPayloadBuilder PayloadFor(string modelId) =>
        _payloads.TryGetValue(modelId, out var p) ? p
        // Pollinations check runs before LooksLikeReplicatePath because "pollinations/foo"
        // also satisfies the owner/name heuristic — without this branch we'd hand Pollinations
        // requests to the Replicate-shaped fallback and the service would reject the payload.
        : IsPollinations(modelId) ? _pollinationsFallback
        : LooksLikeReplicatePath(modelId) ? _replicateFallback
        : throw new ArgumentException($"Unknown model type: {modelId}");

    public ICapabilityProvider CapabilitiesFor(string modelId) =>
        _capabilities.TryGetValue(modelId, out var c) ? c
        : IsPollinations(modelId) ? _pollinationsFallback
        : _replicateFallback;

    private static bool IsPollinations(string modelId) =>
        !string.IsNullOrEmpty(modelId)
        && modelId.StartsWith(ModelConstants.Pollinations.PrefixSlash, StringComparison.Ordinal);

    public IMetadataDescriber? MetadataFor(string modelId) =>
        _metadata.TryGetValue(modelId, out var m) ? m : null;

    public IReadOnlyList<ModelOption> Seeds { get; }

    /// <summary>
    /// Mirrors the original ImageModelFactory.LooksLikeReplicatePath gate — only payload-build
    /// for ids that look like an owner/name path. Anything else throws so misconfigured models
    /// fail loud at generation time instead of silently shipping the conservative dict shape.
    /// </summary>
    private static bool LooksLikeReplicatePath(string modelName)
    {
        var slash = modelName.IndexOf('/');
        return slash > 0 && slash < modelName.Length - 1;
    }

    /// <summary>
    /// Constructs a registry pre-populated with the production descriptor set. Used by tests
    /// (and by the registry's DI registration could call this if it wants a no-DI shortcut).
    /// </summary>
    public static ModelDescriptorRegistry Default()
    {
        var descriptors = ProductionDescriptors();
        return new ModelDescriptorRegistry(
            descriptors.OfType<IPayloadBuilder>(),
            descriptors.OfType<ICapabilityProvider>(),
            descriptors.OfType<IMetadataDescriber>(),
            descriptors.OfType<ICatalogSeedEntry>());
    }

    private static IReadOnlyList<object> ProductionDescriptors() =>
    [
        new Flux11ProDescriptor(),
        new Flux11ProUltraDescriptor(),
        new Flux2Klein4bDescriptor(),
        new Flux2Flex2Descriptor(),
        new Flux2Pro2Descriptor(),
        new Flux2Max2Descriptor(),
        new GptImage15Descriptor(),
        new GptImage2Descriptor(),
        new NanoBanana2Descriptor()
    ];
}
