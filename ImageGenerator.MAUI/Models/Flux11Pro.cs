using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Models;

public class FluxPro11 : FluxBase
{
    public override string Model => "flux-pro-11";

    [JsonPropertyName("prompt_upsampling")]
    public bool PromptUpsampling { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("output_quality")]
    public int OutputQuality { get; set; }
}


