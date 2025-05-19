using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models.Flux;

public abstract class FluxBase : ImageModelBase
{
    [JsonIgnore] // Typically we don't serialize the model name into the flux request body
    public override required string ModelName { get; set; }

    [JsonPropertyName("prompt")]
    [Required(ErrorMessage = "Prompt is mandatory for every Flux request.")]
    [StringLength(2000, ErrorMessage = "Prompt cannot exceed 2000 characters.")]
    public override required string Prompt { get; set; }

    [JsonPropertyName("seed")]
    [Range(0, ValidationConstants.SeedMaxValue, ErrorMessage = "Seed must be between 0 and 4294967295.")]
    public long? Seed { get; set; }

    [JsonPropertyName("output_format")]
    [RegularExpression("jpg|png", ErrorMessage = "Output format must be 'jpg' 'webp' or 'png'.")]
    [Required(ErrorMessage = "Output format is required.")]
    public ImageOutputFormat OutputFormat { get; set; } = ImageOutputFormat.Png;
    
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
    [Range(0, 6, ErrorMessage = "Safety tolerance must be between 0 and 6.")]
    public int SafetyTolerance { get; set; } = 6;
    
    [JsonPropertyName("webhook_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [StringLength(2083)]
    [Url(ErrorMessage = "Webhook URL must be a valid URI.")]
    public string? WebhookUrl { get; set; }

    [JsonPropertyName("webhook_secret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WebhookSecret { get; set; }
}

