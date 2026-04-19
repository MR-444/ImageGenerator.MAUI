using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

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
    int MaxImageInputs = 0)
{
    private static readonly string[] FluxStandard =
        ["1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4"];

    private static readonly string[] Flux11ProWithCustom =
        ["1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4", "custom"];

    // Flux 2 family (klein-4b / flex / pro / max) — schema from GET /v1/models/black-forest-labs/flux-2-klein-4b.
    private static readonly string[] Flux2AspectRatios =
        ["1:1", "16:9", "9:16", "3:2", "2:3", "4:3", "3:4", "5:4", "4:5", "21:9", "9:21", "match_input_image"];

    // openai/gpt-image-1.5 hosted on Replicate only accepts three aspect ratios.
    private static readonly string[] GptImage15AspectRatios =
        ["1:1", "3:2", "2:3"];

    private static readonly string[] GptImage15Quality = ["auto", "low", "medium", "high"];
    private static readonly string[] GptImage15Background = ["auto", "transparent", "opaque"];
    private static readonly string[] GptImage15Moderation = ["auto", "low"];
    private static readonly string[] GptImage15InputFidelity = ["low", "high"];

    // google/nano-banana-2 — 15-value aspect enum + 1K/2K/4K resolution knob.
    private static readonly string[] NanoBanana2AspectRatios =
        ["match_input_image", "1:1", "16:9", "9:16", "21:9", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4", "1:4", "4:1", "1:8", "8:1"];

    private static readonly string[] NanoBanana2Resolutions = ["1K", "2K", "4K"];

    public static ModelCapabilities For(string? modelValue) => modelValue switch
    {
        ModelConstants.Flux.Pro11 => new(
            SafetyTolerance: true, PromptUpsampling: true, OutputQuality: true,
            AspectRatio: true, CustomDimensions: true, Seed: true, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: Flux11ProWithCustom,
            MaxImageInputs: 1),

        ModelConstants.Flux.Pro11Ultra => new(
            SafetyTolerance: true, PromptUpsampling: false, OutputQuality: false,
            AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: FluxStandard,
            ImagePromptStrength: true,
            MaxImageInputs: 1),

        ModelConstants.Flux.Klein4b
            or ModelConstants.Flux.Flex2
            or ModelConstants.Flux.Pro2
            or ModelConstants.Flux.Max2 => new(
                SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
                AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
                AspectRatioLabel: "Aspect ratio", AspectRatios: Flux2AspectRatios,
                MaxImageInputs: 1),

        ModelConstants.OpenAI.GptImage15OnReplicate => new(
            SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
            AspectRatio: true, CustomDimensions: false, Seed: false, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: GptImage15AspectRatios,
            GptQualityOptions: GptImage15Quality,
            GptBackgroundOptions: GptImage15Background,
            GptModerationOptions: GptImage15Moderation,
            GptInputFidelityOptions: GptImage15InputFidelity,
            MaxImageInputs: 10),

        ModelConstants.Google.NanoBanana2 => new(
            SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
            AspectRatio: true, CustomDimensions: false, Seed: false, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: NanoBanana2AspectRatios,
            Resolutions: NanoBanana2Resolutions,
            MaxImageInputs: 14),

        // Any other dynamic Replicate model: no safety_tolerance / prompt_upsampling
        // (newer models generally don't accept them), conservative AR list, seed + image on.
        _ => new(
            SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
            AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: FluxStandard,
            MaxImageInputs: 1)
    };
}
