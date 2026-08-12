using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.Pollinations;

/// <summary>
/// The three Pollinations-hosted OpenAI image models. They differ from the rest of the
/// Pollinations roster in one respect: they honour the `quality` query param. The API documents
/// it as supported by gptimage, gptimage-large and gpt-image-2 only, so it lives here rather
/// than on <see cref="PollinationsDescriptorBase"/>.
/// </summary>
public abstract class PollinationsGptImageDescriptor : PollinationsDescriptorBase
{
    // Pollinations' enum is low/medium/high/hd — note there is no "auto" (unlike the same
    // models on Replicate). "medium" leads deliberately: it is both the API's documented
    // default and the tier the user wants billed, since auto resolves to high-tier cost.
    // RefreshCapabilities snaps an out-of-range selection to the first entry, so ordering
    // medium-first is what makes it the effective default when switching from another model.
    internal static readonly string[] QualityOptions = ["medium", "low", "high", "hd"];

    internal const string DefaultQuality = "medium";

    protected PollinationsGptImageDescriptor(string displayName, string modelId, string serverModelName)
        : base(displayName, modelId, serverModelName) { }

    public override ModelCapabilities Capabilities => base.Capabilities with { GptQualityOptions = QualityOptions };

    // GptQuality is shared app-wide and defaults to "auto" for the Replicate-hosted variants,
    // which Pollinations rejects outright — coerce anything off-menu back to the default rather
    // than letting a 400 reach the user.
    protected override string? ResolveQuality(ImageGenerationParameters p) =>
        QualityOptions.Contains(p.GptQuality) ? p.GptQuality : DefaultQuality;

    public override IEnumerable<string> Lines(ImageGenerationParameters p) =>
        [.. base.Lines(p), $"GptQuality: {ResolveQuality(p)}"];

    public override void Apply(ImageGenerationParameters p, IReadOnlyDictionary<string, string> meta)
    {
        base.Apply(p, meta);
        meta.ApplyString("GptQuality", v => p.GptQuality = v, QualityOptions.Contains);
    }
}

public sealed class PollinationsGptImageDescriptor1Mini : PollinationsGptImageDescriptor
{
    public PollinationsGptImageDescriptor1Mini()
        : base("GPT Image 1 Mini (Pollinations)", ModelConstants.Pollinations.GptImage, "gptimage") { }
}

public sealed class PollinationsGptImageLargeDescriptor : PollinationsGptImageDescriptor
{
    public PollinationsGptImageLargeDescriptor()
        : base("GPT Image 1.5 (Pollinations)", ModelConstants.Pollinations.GptImageLarge, "gptimage-large") { }
}

public sealed class PollinationsGptImage2Descriptor : PollinationsGptImageDescriptor
{
    public PollinationsGptImage2Descriptor()
        : base("GPT Image 2 (Pollinations)", ModelConstants.Pollinations.GptImage2, "gpt-image-2") { }
}
