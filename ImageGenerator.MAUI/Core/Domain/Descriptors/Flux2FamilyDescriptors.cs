using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// Klein4b / Flex / Pro / Max share the same payload shape and capabilities; only ModelId
/// differs. Schema from GET /v1/models/black-forest-labs/flux-2-klein-4b.
/// </summary>
public abstract class Flux2FamilyDescriptor : IPayloadBuilder, ICapabilityProvider
{
    private static readonly string[] AspectRatios =
        ["1:1", "16:9", "9:16", "3:2", "2:3", "4:3", "3:4", "5:4", "4:5", "21:9", "9:21", "match_input_image"];

    public abstract string ModelId { get; }

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: true,
        AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        MaxImageInputs: 1);

    public object Build(ImageGenerationParameters p) => new Dictionary<string, object?>
    {
        ["prompt"] = p.Prompt,
        ["seed"] = p.Seed,
        ["aspect_ratio"] = p.AspectRatio,
        ["output_format"] = p.OutputFormat.ToString().ToLowerInvariant(),
        ["output_quality"] = p.OutputQuality,
        ["images"] = ImageDataUriEncoder.BuildDataUris(p.ImagePrompts, maxCount: 1)
    };
}

// Only Klein4b is in the seed list — matches today's HardcodedCatalogSeed exactly.
// Flex/Pro/Max appear in the picker only after Refresh Models hydrates the catalog.
public sealed class Flux2Klein4bDescriptor : Flux2FamilyDescriptor, ICatalogSeedEntry
{
    public override string ModelId => ModelConstants.Flux.Klein4b;
    public ModelOption Seed => new("Flux 2 Klein 4B", ModelId, ProviderConstants.BlackForestLabs);
}

public sealed class Flux2Flex2Descriptor : Flux2FamilyDescriptor
{
    public override string ModelId => ModelConstants.Flux.Flex2;
}

public sealed class Flux2Pro2Descriptor : Flux2FamilyDescriptor
{
    public override string ModelId => ModelConstants.Flux.Pro2;
}

public sealed class Flux2Max2Descriptor : Flux2FamilyDescriptor
{
    public override string ModelId => ModelConstants.Flux.Max2;
}
