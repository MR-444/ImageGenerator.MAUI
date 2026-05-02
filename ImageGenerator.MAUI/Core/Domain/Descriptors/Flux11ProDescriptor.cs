using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

public sealed class Flux11ProDescriptor : IPayloadBuilder, ICapabilityProvider, IMetadataDescriber, ICatalogSeedEntry
{
    private static readonly string[] AspectRatios =
        ["1:1", "16:9", "9:16", "21:9", "9:21", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4", "custom"];

    public string ModelId => ModelConstants.Flux.Pro11;

    public ModelOption Seed => new("Flux 1.1 Pro", ModelId, ProviderConstants.BlackForestLabs);

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: true, PromptUpsampling: true, OutputQuality: true,
        AspectRatio: true, CustomDimensions: true, Seed: true, ImagePrompt: true,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        MaxImageInputs: 1);

    public object Build(ImageGenerationParameters p) => new Flux11Pro
    {
        ModelName = p.Model,
        Prompt = p.Prompt,
        PromptUpsampling = p.PromptUpsampling,
        Seed = p.Seed,
        Width = p.AspectRatio == "custom" ? p.Width : null,
        Height = p.AspectRatio == "custom" ? p.Height : null,
        AspectRatio = p.AspectRatio,
        ImagePrompt = p.ImagePrompts.FirstOrDefault(),
        SafetyTolerance = p.SafetyTolerance,
        OutputFormat = p.OutputFormat.ToString().ToLowerInvariant(),
        OutputQuality = p.OutputQuality
    };

    public IEnumerable<string> Lines(ImageGenerationParameters p) =>
        [$"Upsampling: {p.PromptUpsampling}"];
}
