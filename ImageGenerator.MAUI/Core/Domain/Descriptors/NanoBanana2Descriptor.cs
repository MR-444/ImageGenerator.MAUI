using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// google/nano-banana-2 — wider AR enum, dedicated resolution knob (1K/2K/4K), no seed,
/// rejects webp output, takes up to 14 input images via image_input.
/// </summary>
public sealed class NanoBanana2Descriptor : IPayloadBuilder, ICapabilityProvider, IMetadataDescriber, ICatalogSeedEntry
{
    private static readonly string[] AspectRatios =
        ["match_input_image", "1:1", "16:9", "9:16", "21:9", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4", "1:4", "4:1", "1:8", "8:1"];

    private static readonly string[] Resolutions = ["1K", "2K", "4K"];

    public string ModelId => ModelConstants.Google.NanoBanana2;

    public ModelOption Seed => new("Nano Banana 2", ModelId, ProviderConstants.Google);

    public ModelCapabilities Capabilities => new(
        SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
        AspectRatio: true, CustomDimensions: false, Seed: false, ImagePrompt: true,
        AspectRatioLabel: "Aspect ratio", AspectRatios: AspectRatios,
        Resolutions: Resolutions,
        MaxImageInputs: 14);

    public object Build(ImageGenerationParameters p) => new Dictionary<string, object?>
    {
        ["prompt"] = p.Prompt,
        ["aspect_ratio"] = p.AspectRatio,
        ["resolution"] = p.Resolution,
        ["output_format"] = p.OutputFormat.ToString().ToLowerInvariant() switch
        {
            "webp" => "jpg",  // nano-banana-2 schema rejects webp.
            var fmt => fmt
        },
        ["image_input"] = ImageDataUriEncoder.BuildDataUris(p.ImagePrompts, maxCount: 14)
        // google_search / image_search stay at API defaults (false/false).
    };

    public IEnumerable<string> Lines(ImageGenerationParameters p) =>
        [$"Resolution: {p.Resolution}"];
}
