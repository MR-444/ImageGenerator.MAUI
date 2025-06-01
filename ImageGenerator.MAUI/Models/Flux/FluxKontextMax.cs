using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models.Flux;

public class FluxKontextMax : FluxBase
{
    public override required string ModelName { get; set; } = "black-forest-labs/flux-kontext-max";

    [JsonPropertyName("input_image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputImage { get; set; }

    [JsonPropertyName("aspect_ratio")]
    public override string AspectRatio { get; set; } = "match_input_image";
} 