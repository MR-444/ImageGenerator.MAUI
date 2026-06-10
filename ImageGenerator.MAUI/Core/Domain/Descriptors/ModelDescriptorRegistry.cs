using ImageGenerator.MAUI.Core.Domain.Descriptors.ComfyUi;
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
    private readonly FallbackComfyUiDescriptor _comfyUiFallback;

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
        _comfyUiFallback = new FallbackComfyUiDescriptor();
    }

    public IPayloadBuilder PayloadFor(string modelId) =>
        _payloads.TryGetValue(modelId, out var p) ? p
        // Pollinations/ComfyUI checks run before LooksLikeReplicatePath because their prefixed
        // ids ("pollinations/foo", "comfyui/foo") also satisfy the owner/name heuristic —
        // without these branches we'd hand their requests to the Replicate-shaped fallback
        // and the service would reject the payload.
        : ModelConstants.Pollinations.IsId(modelId) ? _pollinationsFallback
        : ModelConstants.ComfyUi.IsId(modelId) ? _comfyUiFallback
        : LooksLikeReplicatePath(modelId) ? _replicateFallback
        : throw new ArgumentException($"Unknown model type: {modelId}");

    public ICapabilityProvider CapabilitiesFor(string modelId) =>
        _capabilities.TryGetValue(modelId, out var c) ? c
        : ModelConstants.Pollinations.IsId(modelId) ? _pollinationsFallback
        : ModelConstants.ComfyUi.IsId(modelId) ? _comfyUiFallback
        : _replicateFallback;

    public IMetadataDescriber? MetadataFor(string modelId) =>
        _metadata.TryGetValue(modelId, out var m) ? m
        // ComfyUI ids are never in _metadata (no per-workflow descriptors); route them to the
        // fallback so the workflow name lands in the saved image's metadata.
        : ModelConstants.ComfyUi.IsId(modelId) ? _comfyUiFallback
        : null;

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
        new NanoBanana2Descriptor(),
        new IdeogramV4BalancedDescriptor(),
        new IdeogramV4TurboDescriptor(),
        new IdeogramV4QualityDescriptor()
    ];
}
