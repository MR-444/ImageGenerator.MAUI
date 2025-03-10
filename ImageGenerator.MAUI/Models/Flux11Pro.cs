using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models;

public class FluxPro11 : FluxBase
{
    [JsonPropertyName("prompt_upsampling")]
    public bool PromptUpsampling { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("output_quality")]
    public int OutputQuality { get; set; }
}


