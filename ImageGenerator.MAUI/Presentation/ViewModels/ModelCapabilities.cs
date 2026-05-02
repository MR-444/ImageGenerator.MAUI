namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Pure data shape declaring which UI knobs a model exposes. The per-model values now live
/// on each descriptor (Core/Domain/Descriptors/*Descriptor.cs); look up via
/// IModelDescriptorRegistry.CapabilitiesFor(modelId).Capabilities.
/// </summary>
public readonly record struct ModelCapabilities(
    bool SafetyTolerance,
    bool PromptUpsampling,
    bool OutputQuality,
    bool AspectRatio,
    bool CustomDimensions,
    bool Seed,
    bool ImagePrompt,
    string AspectRatioLabel,
    IReadOnlyList<string> AspectRatios,
    IReadOnlyList<string>? Resolutions = null,
    IReadOnlyList<string>? GptQualityOptions = null,
    IReadOnlyList<string>? GptBackgroundOptions = null,
    IReadOnlyList<string>? GptModerationOptions = null,
    IReadOnlyList<string>? GptInputFidelityOptions = null,
    bool ImagePromptStrength = false,
    int MaxImageInputs = 0);
