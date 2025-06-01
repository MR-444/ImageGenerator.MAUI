using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Models.Flux;

public class FluxKontextPro : FluxBase
{
    public override required string ModelName { get; set; } = "black-forest-labs/flux-kontext-pro";

    [JsonPropertyName("input_image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputImage { get; set; }

    [JsonPropertyName("aspect_ratio")]
    public override string AspectRatio { get; set; } = "match_input_image";
} 