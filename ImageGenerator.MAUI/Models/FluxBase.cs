using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models;

public abstract class FluxBase
{
    [JsonPropertyName("prompt")]
    [Required(ErrorMessage = "Prompt is mandatory for every Flux request.")]
    [StringLength(2000, ErrorMessage = "Prompt cannot exceed 2000 characters.")]
    public string Prompt { get; set; }

    [JsonPropertyName("seed")]
    [Range(0, ValidationConstants.SeedMaxValue, ErrorMessage = "Seed must be between 0 and 4294967295.")]
    public long? Seed { get; set; }

    [JsonPropertyName("output_format")]
    [RegularExpression("jpg|png", ErrorMessage = "Output format must be 'jpg' or 'png'.")]
    [Required(ErrorMessage = "Output format is required.")]
    public virtual ImageOutputFormat OutputFormat { get; set; } = ImageOutputFormat.Png;
    
    [JsonPropertyName("aspect_ratio")]
    public string AspectRatio { get; set; } = "1:1";

    [JsonPropertyName("image_prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImagePrompt
    {
        get => _imagePrompt;
        set => _imagePrompt = string.IsNullOrWhiteSpace(value) ? null : value;
    }
    private string? _imagePrompt;

    [JsonPropertyName("safety_tolerance")]
    [Range(1, 6, ErrorMessage = "Safety tolerance must be between 1 and 6.")]
    public int SafetyTolerance { get; set; } = 6;
}

