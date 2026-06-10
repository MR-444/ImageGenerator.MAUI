using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// ideogram-v4-balanced / -turbo / -quality share one Replicate input schema:
/// <list type="bullet">
///   <item><c>prompt</c> OR <c>json_prompt</c> (mutually exclusive; the <c>UseJsonPrompt</c>
///   toggle picks which one the single prompt box maps to). Magic Prompt is auto-applied.</item>
///   <item><c>resolution</c> — optional; omit (the "Auto" sentinel) to let Ideogram pick the AR.</item>
///   <item><c>enable_copyright_detection</c> — optional bool.</item>
/// </list>
/// There is no aspect_ratio / seed / output_format input and the model only emits PNG, so the
/// generic knobs are all off and <c>OutputFormatSelectable</c> is false (ImageFileService still
/// saves PNG). <c>rendering_speed</c> is fixed per variant, so it isn't an input here.
///
/// json_prompt is sent as a JSON **string** — Replicate's cog types the field as `string`, not a
/// nested object (confirmed by a 422 "input.json_prompt: Expected: string, given: object"). The
/// UI validates the box is real JSON before generation, so what ships is a valid JSON string.
/// </summary>
public abstract class IdeogramV4Descriptor : IPayloadBuilder, ICapabilityProvider, IMetadataDescriber, ICatalogSeedEntry
{
    // Sentinel for "let Ideogram choose the aspect ratio" — selecting it omits `resolution`.
    public const string AutoResolution = "Auto";

    // Ideogram V4 resolutions (from the generate-v4 schema). "Auto" first so RefreshCapabilities
    // defaults Parameters.Resolution to the omit-sentinel when switching to an Ideogram model.
    private static readonly string[] IdeogramResolutions =
    [
        AutoResolution,
        "2048x2048", "1440x2880", "2880x1440", "1664x2496", "2496x1664", "1792x2240", "2240x1792",
        "1440x2560", "2560x1440", "1600x2560", "2560x1600", "1728x2304", "2304x1728", "1296x3168",
        "3168x1296", "1152x2944", "2944x1152", "1248x3328", "3328x1248", "1280x3072", "3072x1280",
        "1024x3072", "3072x1024"
    ];

    // The structure editor offers the same resolution choice on its canvas card, so the
    // single source of truth for the V4 list lives here.
    public static IReadOnlyList<string> AllResolutions => IdeogramResolutions;

    // AspectRatio is hidden, but RefreshCapabilities indexes AspectRatios[0], so keep it non-empty.
    private static readonly string[] AspectRatios = ["1:1"];

    public abstract string ModelId { get; }
    public abstract ModelOption Seed { get; }

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
        AspectRatio: false, CustomDimensions: false, Seed: false, ImagePrompt: false,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        Resolutions: IdeogramResolutions,
        MaxImageInputs: 0,
        OutputFormatSelectable: false,
        IdeogramOptions: true,
        JsonPromptEditor: true);

    public object Build(ImageGenerationParameters p)
    {
        var payload = new Dictionary<string, object?>();

        if (p.UseJsonPrompt)
            payload["json_prompt"] = p.Prompt;
        else
            payload["prompt"] = p.Prompt;

        if (!string.IsNullOrWhiteSpace(p.Resolution) && p.Resolution != AutoResolution)
            payload["resolution"] = p.Resolution;

        if (p.EnableCopyrightDetection)
            payload["enable_copyright_detection"] = true;

        return payload;
    }

    public IEnumerable<string> Lines(ImageGenerationParameters p) =>
    [
        $"Resolution: {p.Resolution}",
        $"JsonPrompt: {p.UseJsonPrompt}",
        $"CopyrightDetection: {p.EnableCopyrightDetection}"
    ];
}

public sealed class IdeogramV4BalancedDescriptor : IdeogramV4Descriptor
{
    public override string ModelId => ModelConstants.Ideogram.V4Balanced;
    public override ModelOption Seed => new("Ideogram V4 Balanced", ModelId, ProviderConstants.Replicate);
}

public sealed class IdeogramV4TurboDescriptor : IdeogramV4Descriptor
{
    public override string ModelId => ModelConstants.Ideogram.V4Turbo;
    public override ModelOption Seed => new("Ideogram V4 Turbo", ModelId, ProviderConstants.Replicate);
}

public sealed class IdeogramV4QualityDescriptor : IdeogramV4Descriptor
{
    public override string ModelId => ModelConstants.Ideogram.V4Quality;
    public override ModelOption Seed => new("Ideogram V4 Quality", ModelId, ProviderConstants.Replicate);
}
