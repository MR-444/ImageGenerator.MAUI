using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Models;

public class Flux11ProUltra : FluxBase
{
    [JsonPropertyName("raw")]
    public bool Raw { get; set; } = false;

    [JsonPropertyName("image_prompt_strength")]
    [Range(0, 1, ErrorMessage = "Image Prompt Strength must be between 0 and 1.")]
    public double ImagePromptStrength { get; set; } = 0.1;
}

