using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;

public class Flux11ProUltra : FluxBase
{
    [JsonPropertyName("raw")]
    public bool Raw { get; set; } = false;

    // Default matches ImageGenerationParameters.ImagePromptStrength. The factory always
    // copies from parameters so this default never wins in practice, but a divergent
    // value here is a confusing landmine when reading either file in isolation.
    [JsonPropertyName("image_prompt_strength")]
    [Range(0.0, 1.0, ErrorMessage = "Image Prompt Strength must be between 0 and 1.")]
    public double ImagePromptStrength { get; set; } = 0.5;
}