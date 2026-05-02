using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;

public abstract class FluxBase
{
    [JsonIgnore] // Not part of the Flux request body; combining [JsonIgnore] with `required`
    // trips STJ metadata validation in .NET 10, so we rely on the factory setting this explicitly.
    public string ModelName { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    [Required(ErrorMessage = "Prompt is mandatory for every Flux request.")]
    [StringLength(2000, ErrorMessage = "Prompt cannot exceed 2000 characters.")]
    public required string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("seed")]
    [Range(0, ValidationConstants.SeedMaxValue, ErrorMessage = "Seed must be between 0 and 4294967295.")]
    public long? Seed { get; set; }

    [JsonPropertyName("output_format")]
    [RegularExpression("webp|jpg|png", ErrorMessage = "Output format must be 'webp', 'jpg', or 'png'.")]
    public string OutputFormat { get; set; } = "png";
    
    [JsonPropertyName("aspect_ratio")]
    public virtual string AspectRatio { get; set; } = "1:1";

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
    public int SafetyTolerance { get; set; } = ValidationConstants.SafetyMax;

    // Nullable so models that don't accept output_quality (Flux 1.1 Pro Ultra) can leave it
    // unset and have the field omitted from the request body. Flux 1.1 Pro assigns it
    // explicitly in the factory, so its requests still carry the value.
    [JsonPropertyName("output_quality")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Range(0, 100, ErrorMessage = "Output quality must be between 0 and 100.")]
    public int? OutputQuality { get; set; }

    [JsonPropertyName("prompt_upsampling")]
    public bool PromptUpsampling { get; set; }
}

