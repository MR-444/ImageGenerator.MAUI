using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// gpt-image-1.5 and gpt-image-2 hosted on Replicate share an identical wire payload and
/// capability set. The audit (2026-05-02) noted gpt-image-2's published schema omits
/// input_fidelity but the model is believed to accept it regardless; revisit if Replicate
/// starts 422-ing on that field for gpt-image-2.
/// </summary>
public abstract class GptImageOnReplicateDescriptor : IPayloadBuilder, ICapabilityProvider, IMetadataDescriber, ICatalogSeedEntry
{
    private static readonly string[] AspectRatios = ["1:1", "3:2", "2:3"];
    private static readonly string[] Quality = ["auto", "low", "medium", "high"];
    private static readonly string[] Background = ["auto", "transparent", "opaque"];
    private static readonly string[] Moderation = ["auto", "low"];
    private static readonly string[] InputFidelity = ["low", "high"];

    public abstract string ModelId { get; }
    public abstract ModelOption Seed { get; }

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
        AspectRatio: true, CustomDimensions: false, Seed: false, ImagePrompt: true,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        GptQualityOptions: Quality,
        GptBackgroundOptions: Background,
        GptModerationOptions: Moderation,
        GptInputFidelityOptions: InputFidelity,
        MaxImageInputs: 10);

    public object Build(ImageGenerationParameters p) => new Dictionary<string, object?>
    {
        ["prompt"] = p.Prompt,
        ["aspect_ratio"] = p.AspectRatio,
        ["output_format"] = p.OutputFormat.ToString().ToLowerInvariant() switch
        {
            "jpg" => "jpeg",
            var fmt => fmt
        },
        ["output_compression"] = p.OutputQuality,
        ["quality"] = p.GptQuality,
        ["background"] = p.GptBackground,
        ["moderation"] = p.GptModeration,
        ["input_fidelity"] = p.GptInputFidelity,
        ["input_images"] = ImageDataUriEncoder.BuildDataUris(p.ImagePrompts, maxCount: 10)
    };

    public IEnumerable<string> Lines(ImageGenerationParameters p) =>
    [
        $"GptQuality: {p.GptQuality}",
        $"GptBackground: {p.GptBackground}",
        $"GptModeration: {p.GptModeration}",
        $"GptInputFidelity: {p.GptInputFidelity}"
    ];
}

public sealed class GptImage15Descriptor : GptImageOnReplicateDescriptor
{
    public override string ModelId => ModelConstants.OpenAI.GptImage15OnReplicate;
    public override ModelOption Seed => new("GPT Image 1.5", ModelId, ProviderConstants.OpenAIOnReplicate);
}

public sealed class GptImage2Descriptor : GptImageOnReplicateDescriptor
{
    public override string ModelId => ModelConstants.OpenAI.GptImage2OnReplicate;
    public override ModelOption Seed => new("GPT Image 2", ModelId, ProviderConstants.OpenAIOnReplicate);
}
