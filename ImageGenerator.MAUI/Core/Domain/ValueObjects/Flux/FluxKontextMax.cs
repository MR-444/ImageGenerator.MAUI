using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.Flux;

public class FluxKontextMax : FluxBase
{
    [JsonPropertyName("input_image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputImage { get; set; }

    [JsonPropertyName("aspect_ratio")]
    public override string AspectRatio 
    { 
        get => InputImage != null ? "match_input_image" : base.AspectRatio;
        set => base.AspectRatio = value;
    }
} 