using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// The declared input vocabulary of ONE hosted gpt-image model, transcribed from that model's own
/// schema (snapshots in <c>documents/gpt-image-*_input_schema.txt</c>).
///
/// This exists because the hosted gpt-image models do NOT share a vocabulary. Aspect ratio alone
/// went from three values to eighteen between gpt-image-1.5 and gpt-image-2.6-sunburst, and the
/// field set itself moved: <c>input_fidelity</c> is gone, <c>complexity</c> appeared. A descriptor
/// base class holding one shared static list cannot express that, and the failure is quiet: cog
/// silently DROPS inputs a model does not declare (replicate/cog#2943, 2026-04-15), so a field sent
/// under a name the model never had is a knob doing nothing, not an error. Only an out-of-enum value
/// on a field the model DOES declare comes back as a 422 — which is how this was found.
/// </summary>
/// <param name="AspectRatios">Values for <c>aspect_ratio</c>, in the schema's own order. The first
/// entry doubles as the UI's last-resort pick (see GeneratorViewModel.RefreshCapabilities), so it
/// should equal the model's server default.</param>
/// <param name="Quality">Values for <c>quality</c>. Kept with <c>auto</c> first for the same reason.</param>
/// <param name="InputFidelity">Non-null only when the schema declares <c>input_fidelity</c>.</param>
/// <param name="Complexity">Non-null only when the schema declares <c>complexity</c>.</param>
/// <param name="MaxImageInputs">How many attached images are forwarded to <c>input_images</c>.</param>
public sealed record GptImageSchema(
    string[] AspectRatios,
    string[] Quality,
    string[] Background,
    string[] Moderation,
    string[]? InputFidelity,
    string[]? Complexity,
    int MaxImageInputs = 10)
{
    // GET /v1/models/openai/gpt-image-1.5 — declares input_fidelity, three aspect ratios.
    public static GptImageSchema GptImage15 { get; } = new(
        AspectRatios: ["1:1", "3:2", "2:3"],
        Quality: ["auto", "low", "medium", "high"],
        Background: ["auto", "transparent", "opaque"],
        Moderation: ["auto", "low"],
        InputFidelity: ["low", "high"],
        Complexity: null);

    // GET /v1/models/openai/gpt-image-2 — same vocabulary as 1.5 MINUS input_fidelity. The old
    // shared base sent input_fidelity here anyway on the theory the model accepted it regardless;
    // the snapshot says otherwise, so the knob is now hidden rather than decorative.
    public static GptImageSchema GptImage2 { get; } = new(
        AspectRatios: ["1:1", "3:2", "2:3"],
        Quality: ["auto", "low", "medium", "high"],
        Background: ["auto", "transparent", "opaque"],
        Moderation: ["auto", "low"],
        InputFidelity: null,
        Complexity: null);

    // GET /v1/models/openai/gpt-image-2.6-sunburst — 18 aspect ratios (named ratios plus explicit
    // pixel sizes plus "auto"), quality gains xhigh/max, input_fidelity is gone, complexity is new.
    // gif/svg output exists here too and is deliberately NOT exposed: ImageOutputFormat has no
    // gif/svg member and ImageFileService re-encodes everything that is not PNG through ImageSharp,
    // which would flatten an animation and destroy a vector.
    public static GptImageSchema GptImage26Sunburst { get; } = new(
        AspectRatios:
        [
            "1:1", "3:2", "2:3", "4:3", "3:4", "16:9", "9:16", "auto",
            "1024x1024", "1536x1024", "1024x1536", "1536x1152", "1152x1536",
            "2048x2048", "2048x1152", "1152x2048", "3840x2160", "2160x3840"
        ],
        Quality: ["auto", "low", "medium", "high", "xhigh", "max"],
        Background: ["auto", "transparent", "opaque"],
        Moderation: ["auto", "low"],
        InputFidelity: null,
        Complexity: ["low", "medium", "high"]);
}

/// <summary>
/// A hosted (Replicate) gpt-image model. Everything model-specific lives in
/// <see cref="Schema"/>; the payload, the capability declaration and the image-metadata round-trip
/// are all derived from it, so a new model in this family is normally one profile plus one subclass.
/// </summary>
public abstract class GptImageOnReplicateDescriptor : IPayloadBuilder, ICapabilityProvider, IMetadataDescriber, ICatalogSeedEntry
{
    /// <summary>The vocabulary this model declares. Nothing below may assume a field exists.</summary>
    protected abstract GptImageSchema Schema { get; }

    public abstract string ModelId { get; }
    public abstract ModelOption Seed { get; }

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
        AspectRatio: true, CustomDimensions: false, Seed: false, ImagePrompt: true,
        AspectRatioLabel: "Aspect ratio", AspectRatios: Schema.AspectRatios,
        GptQualityOptions: Schema.Quality,
        GptBackgroundOptions: Schema.Background,
        GptModerationOptions: Schema.Moderation,
        GptInputFidelityOptions: Schema.InputFidelity,
        GptComplexityOptions: Schema.Complexity,
        MaxImageInputs: Schema.MaxImageInputs);

    public object Build(ImageGenerationParameters p)
    {
        // Member order is irrelevant — Replicate resolves input fields by name.
        var input = new Dictionary<string, object?>
        {
            ["prompt"] = p.Prompt,
            ["aspect_ratio"] = p.AspectRatio,
            ["output_format"] = p.OutputFormat.ToString().ToLowerInvariant() switch
            {
                "jpg" => "jpeg",
                var fmt => fmt
            },
            // The UI slider is "output quality"; this family's field is a compression percentage.
            ["output_compression"] = p.OutputQuality,
            ["quality"] = p.GptQuality,
            ["background"] = p.GptBackground,
            ["moderation"] = p.GptModeration,
            ["input_images"] = ImageDataUriEncoder.BuildDataUris(p.ImagePrompts, maxCount: Schema.MaxImageInputs)
        };

        // Declared-or-absent, never "send it and hope cog ignores it": an undeclared field is
        // dropped server-side, so the only signal that it did nothing is a user noticing.
        if (Schema.InputFidelity is not null) input["input_fidelity"] = p.GptInputFidelity;
        if (Schema.Complexity is not null) input["complexity"] = p.GptComplexity;

        return input;
    }

    public IEnumerable<string> Lines(ImageGenerationParameters p)
    {
        var lines = new List<string>
        {
            $"GptQuality: {p.GptQuality}",
            $"GptBackground: {p.GptBackground}",
            $"GptModeration: {p.GptModeration}"
        };
        if (Schema.InputFidelity is not null) lines.Add($"GptInputFidelity: {p.GptInputFidelity}");
        if (Schema.Complexity is not null) lines.Add($"GptComplexity: {p.GptComplexity}");
        return lines;
    }

    public void Apply(ImageGenerationParameters p, IReadOnlyDictionary<string, string> meta)
    {
        // Only adopt values still offered by this model, so an out-of-range string from an
        // older save can't slip past the picker into the Build payload.
        meta.ApplyString("GptQuality", v => p.GptQuality = v, Schema.Quality.Contains);
        meta.ApplyString("GptBackground", v => p.GptBackground = v, Schema.Background.Contains);
        meta.ApplyString("GptModeration", v => p.GptModeration = v, Schema.Moderation.Contains);
        if (Schema.InputFidelity is { } fidelity)
            meta.ApplyString("GptInputFidelity", v => p.GptInputFidelity = v, fidelity.Contains);
        if (Schema.Complexity is { } complexity)
            meta.ApplyString("GptComplexity", v => p.GptComplexity = v, complexity.Contains);
    }
}

public sealed class GptImage15Descriptor : GptImageOnReplicateDescriptor
{
    protected override GptImageSchema Schema => GptImageSchema.GptImage15;
    public override string ModelId => ModelConstants.OpenAI.GptImage15OnReplicate;
    public override ModelOption Seed => new("GPT Image 1.5", ModelId, ProviderConstants.Replicate);
}

public sealed class GptImage2Descriptor : GptImageOnReplicateDescriptor
{
    protected override GptImageSchema Schema => GptImageSchema.GptImage2;
    public override string ModelId => ModelConstants.OpenAI.GptImage2OnReplicate;
    public override ModelOption Seed => new("GPT Image 2", ModelId, ProviderConstants.Replicate);
}

/// <summary>
/// <c>openai/gpt-image-2.6-sunburst</c>. Registered explicitly rather than left to
/// <see cref="FallbackReplicateDescriptor"/>: the fallback sends <c>images</c> / <c>seed</c> /
/// <c>output_quality</c> — none of which this model declares — and offers aspect ratios it rejects.
/// </summary>
public sealed class GptImage26SunburstDescriptor : GptImageOnReplicateDescriptor
{
    protected override GptImageSchema Schema => GptImageSchema.GptImage26Sunburst;
    public override string ModelId => ModelConstants.OpenAI.GptImage26SunburstOnReplicate;
    public override ModelOption Seed => new("GPT Image 2.6 Sunburst", ModelId, ProviderConstants.Replicate);
}
