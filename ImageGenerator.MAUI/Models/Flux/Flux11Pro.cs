using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models.Flux;

public class Flux11Pro : FluxBase
{
    public override required string ModelName {get ;set;} = "black-forest-labs/flux-1.1-pro";

    [JsonPropertyName("prompt_upsampling")]
    public bool PromptUpsampling { get; set; }

    [JsonPropertyName("width")]
    [Range(ValidationConstants.ImageWidthMin, ValidationConstants.ImageWidthMax, ErrorMessage = "Width must be between 64 and 2048.")]
    public int Width { get; set; } = ValidationConstants.ImageWidthMax / 2;

    [JsonPropertyName("height")]
    [Range(ValidationConstants.ImageHeightMin, ValidationConstants.ImageHeightMax, ErrorMessage = "Height must be between 64 and 2048.")]
    public int Height { get; set; } = ValidationConstants.ImageHeightMax / 2;

    [JsonPropertyName("output_quality")]
    [Range(ValidationConstants.OutputQualityMin, ValidationConstants.OutputQualityMax, ErrorMessage = "Output quality must be between 1 and 100.")]
    public int OutputQuality { get; set; } = ValidationConstants.OutputQualityMax;
}