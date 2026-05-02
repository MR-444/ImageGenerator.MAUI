using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

public sealed class Flux11ProUltraDescriptor : IPayloadBuilder, ICapabilityProvider, IMetadataDescriber, ICatalogSeedEntry
{
    private static readonly string[] AspectRatios =
        ["1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4"];

    public string ModelId => ModelConstants.Flux.Pro11Ultra;

    public ModelOption Seed => new("Flux 1.1 Pro Ultra", ModelId, ProviderConstants.BlackForestLabs);

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: true, PromptUpsampling: false, OutputQuality: false,
        AspectRatio: true, CustomDimensions: false, Seed: true, ImagePrompt: true,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        ImagePromptStrength: true,
        MaxImageInputs: 1);

    public object Build(ImageGenerationParameters p) => new Flux11ProUltra
    {
        ModelName = p.Model,
        Prompt = p.Prompt,
        Seed = p.Seed,
        AspectRatio = p.AspectRatio,
        ImagePrompt = p.ImagePrompts.FirstOrDefault(),
        SafetyTolerance = p.SafetyTolerance,
        OutputFormat = p.OutputFormat.ToString().ToLowerInvariant(),
        Raw = p.Raw,
        ImagePromptStrength = p.ImagePromptStrength
    };

    public IEnumerable<string> Lines(ImageGenerationParameters p) =>
    [
        $"Raw: {p.Raw}",
        $"ImagePromptStrength: {p.ImagePromptStrength}"
    ];
}
