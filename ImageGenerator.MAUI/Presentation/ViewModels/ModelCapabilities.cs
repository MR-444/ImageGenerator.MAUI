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
    IReadOnlyList<string> AspectRatios)
{
    // Flux models share most ratios; Dev/Schnell drop 3:4 and 4:3 (not in Replicate's enum).
    private static readonly string[] FluxStandard =
        ["1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4"];

    private static readonly string[] FluxDevSchnell =
        ["1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:5", "5:4"];

    private static readonly string[] FluxKontext =
        ["match_input_image", "1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4"];

    private static readonly string[] Flux11ProWithCustom =
        ["1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4", "custom"];

    // OpenAI gpt-image-1 (native) accepts specific size strings, not ratios.
    private static readonly string[] OpenAiSizes =
        ["auto", "1024x1024", "1536x1024", "1024x1536"];

    // Flux 2 family (klein-4b / flex / pro / max) — schema from GET /v1/models/black-forest-labs/flux-2-klein-4b.
    private static readonly string[] Flux2AspectRatios =
        ["1:1", "16:9", "9:16", "3:2", "2:3", "4:3", "3:4", "5:4", "4:5", "21:9", "9:21", "match_input_image"];

    // openai/gpt-image-1.5 hosted on Replicate only accepts three aspect ratios.
    private static readonly string[] GptImage15AspectRatios =
        ["1:1", "3:2", "2:3"];

    public static ModelCapabilities For(string? modelValue) => modelValue switch
    {
        ModelConstants.Flux.Pro11 => new(
            SafetyTolerance: true, PromptUpsampling: true, OutputQuality: true,
            AspectRatio: true, CustomDimensions: true, Seed: true, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: Flux11ProWithCustom),

        ModelConstants.Flux.Pro11Ultra => new(
            SafetyTolerance: true, PromptUpsampling: false, OutputQuality: false,
            AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: FluxStandard),

        ModelConstants.Flux.Dev => new(
            SafetyTolerance: true, PromptUpsampling: false, OutputQuality: true,
            AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: FluxDevSchnell),

        ModelConstants.Flux.Schnell => new(
            SafetyTolerance: true, PromptUpsampling: false, OutputQuality: true,
            AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: false,
            AspectRatioLabel: "Aspect ratio", AspectRatios: FluxDevSchnell),

        ModelConstants.Flux.KontextMax or ModelConstants.Flux.KontextPro => new(
            SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
            AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: FluxKontext),

        ModelConstants.OpenAI.GptImage1 => new(
            SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
            AspectRatio: true, CustomDimensions: false, Seed: false, ImagePrompt: false,
            AspectRatioLabel: "Size", AspectRatios: OpenAiSizes),

        ModelConstants.Flux.Klein4b
            or ModelConstants.Flux.Flex2
            or ModelConstants.Flux.Pro2
            or ModelConstants.Flux.Max2 => new(
                SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
                AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
                AspectRatioLabel: "Aspect ratio", AspectRatios: Flux2AspectRatios),

        ModelConstants.OpenAI.GptImage15OnReplicate => new(
            SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
            AspectRatio: true, CustomDimensions: false, Seed: false, ImagePrompt: true,
            AspectRatioLabel: "Aspect ratio", AspectRatios: GptImage15AspectRatios),

        _ => IsNativeOpenAi(modelValue)
            ? new(
                SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
                AspectRatio: true, CustomDimensions: false, Seed: false, ImagePrompt: false,
                AspectRatioLabel: "Size", AspectRatios: OpenAiSizes)
            // Any other dynamic Replicate model: no safety_tolerance / prompt_upsampling
            // (newer models generally don't accept them), conservative AR list, seed + image on.
            : new(
                SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
                AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
                AspectRatioLabel: "Aspect ratio", AspectRatios: FluxStandard)
    };

    // Legacy native OpenAI path uses capital-A "openAI/"; Replicate-hosted OpenAI uses lowercase "openai/".
    private static bool IsNativeOpenAi(string? modelValue) =>
        modelValue?.StartsWith("openAI/", StringComparison.Ordinal) == true;
}
