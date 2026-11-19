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
        Seeds = (ModelOption[])[.. seeds.Select(s => s.Seed)];
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
        // A Replicate id with no descriptor is a model this build cannot describe, and there is no
        // longer any object that could build a request for it. ReplicateImageGenerationService
        // refuses such models before it spends anything; this is the same rule one layer down, for
        // any caller that forgot it.
        : throw new ArgumentException($"No descriptor for model: {modelId}");

    // The only remaining user of the Replicate fallback: capabilities are what the pickers need to
    // stay honest about a model this build will not call.
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

    /// <inheritdoc/>
    public bool IsSchemaKnown(string modelId) =>
        // A registered descriptor is what "known" means, and _payloads is the dictionary the
        // generation path consults, so it is the one to trust.
        _payloads.ContainsKey(modelId)
        // The two providers whose fallback is complete rather than a guess: Pollinations takes any
        // model name over one uniform API, and a ComfyUI "model" is a workflow file whose fields
        // come from the user's own export. Neither is a gap in this build, so neither is reported
        // as one — same order of checks as PayloadFor above.
        || ModelConstants.Pollinations.IsId(modelId)
        || ModelConstants.ComfyUi.IsId(modelId);

    public IReadOnlyList<ModelOption> Seeds { get; }

    /// <inheritdoc/>
    // The key collection, not a copy: nothing here can change what is registered, and the coverage check
    // that reads it runs on every catalog load.
    public IReadOnlyCollection<string> DescribedModelIds => _payloads.Keys;

    /// <summary>
    /// Constructs a registry pre-populated with the production descriptor set. Used by tests,
    /// and by <c>MauiProgram</c>, which registers exactly this list — the two sets are one list
    /// on purpose. They used to be written out twice and drifted once (the typed Pollinations
    /// descriptors were missing from <see cref="Default()"/>, so the tests ran a different
    /// production set than the app shipped; see
    /// ModelDescriptorRegistryTests.Default_RegistersPollinationsDescriptors).
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

    /// <summary>
    /// The one production descriptor set: every model with a hand-written descriptor, in picker
    /// order. <see cref="Default()"/> wraps it for tests; <c>MauiProgram</c> registers it with DI.
    /// Adding a model = one descriptor class + one line here.
    /// </summary>
    public static IReadOnlyList<object> ProductionDescriptors() =>
    (object[])[
        new Flux11ProDescriptor(),
        new Flux11ProUltraDescriptor(),
        new Flux2Klein4bDescriptor(),
        new Flux2Flex2Descriptor(),
        new Flux2Pro2Descriptor(),
        new Flux2Max2Descriptor(),
        new GptImage15Descriptor(),
        new GptImage2Descriptor(),
        new GptImage26SunburstDescriptor(),
        new NanoBanana2Descriptor(),
        new IdeogramV4BalancedDescriptor(),
        new IdeogramV4TurboDescriptor(),
        new IdeogramV4QualityDescriptor(),
        new PollinationsFluxDescriptor(),
        new PollinationsZimageDescriptor(),
        new PollinationsQwenImageDescriptor(),
        new PollinationsGptImageDescriptor1Mini(),
        new PollinationsGptImageLargeDescriptor(),
        new PollinationsGptImage2Descriptor()
    ];
}
