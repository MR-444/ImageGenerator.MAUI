using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Models;

public class Flux11ProUltra : FluxBase
{
    public override string Model => "flux-1.1-pro-ultra";
    
    [JsonPropertyName("raw")]
    public bool Raw { get; set; } = false;

    [JsonPropertyName("image_prompt_strength")]
    [Range(0.0, 1.0, ErrorMessage = "Image Prompt Strength must be between 0 and 1.")]
    public double ImagePromptStrength { get; set; } = 0.1;
}

