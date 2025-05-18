using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Models.Flux;

public class FluxPro11 : FluxBase
{
    public override string Model => "flux-pro-11";

    [JsonPropertyName("prompt_upsampling")]
    public bool PromptUpsampling { get; set; }

    [JsonPropertyName("width")]
    [Range(256, 1440, ErrorMessage = "Width must be between 256 and 1440.")]
    public int Width { get; set; } = 1024;


    [JsonPropertyName("height")]
    [Range(256, 1440, ErrorMessage = "Height must be between 256 and 1440.")]
    public int Height { get; set; } = 768;


    [JsonPropertyName("output_quality")]
    public int OutputQuality { get; set; }
}


