using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;

public class FluxKontextPro : FluxBase
{
    public override required string ModelName { get; set; } = "black-forest-labs/flux-kontext-pro";

    [JsonPropertyName("input_image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Url(ErrorMessage = "Input image must be a valid URL.")]
    public string? InputImage { get; set; }

    [JsonPropertyName("aspect_ratio")]
    public override string AspectRatio 
    { 
        get => InputImage != null ? "match_input_image" : base.AspectRatio;
        set => base.AspectRatio = value;
    }
} 