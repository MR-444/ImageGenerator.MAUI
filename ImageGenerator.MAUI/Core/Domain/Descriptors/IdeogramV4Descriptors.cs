using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// ideogram-v4-balanced / -turbo / -quality share one Replicate input schema: a natural-language
/// <c>prompt</c> (or a structured <c>json_prompt</c>), an optional <c>resolution</c> (omit to let
/// Ideogram auto-pick the aspect ratio), and <c>enable_copyright_detection</c>. Magic Prompt is
/// applied automatically — there is no aspect_ratio / seed / output_format / output_quality /
/// image-input field (unlike Flux/GPT), so the conservative FallbackReplicateDescriptor would
/// 422 here. We send <c>prompt</c> only: the single guaranteed-valid field. The saved file is
/// re-encoded to the user's chosen format by ImageFileService regardless of what Ideogram returns.
///
/// The <c>resolution</c> picker is intentionally omitted until its exact Replicate enum is
/// confirmed from https://replicate.com/ideogram-ai/ideogram-v4-quality/api/schema#input-schema
/// — shipping a wrong resolution value would 422. Auto aspect ratio is a fine default meanwhile.
/// </summary>
public abstract class IdeogramV4Descriptor : IPayloadBuilder, ICapabilityProvider, ICatalogSeedEntry
{
    // AspectRatio is hidden (the model auto-selects), but RefreshCapabilities indexes
    // AspectRatios[0], so this must stay non-empty. The value is never sent to the API.
    private static readonly string[] AspectRatios = ["1:1"];

    public abstract string ModelId { get; }
    public abstract ModelOption Seed { get; }

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
        AspectRatio: false, CustomDimensions: false, Seed: false, ImagePrompt: false,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        MaxImageInputs: 0);

    public object Build(ImageGenerationParameters p) => new Dictionary<string, object?>
    {
        ["prompt"] = p.Prompt
    };
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
