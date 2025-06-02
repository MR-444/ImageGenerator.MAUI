using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Models.Flux;

public class Flux11Pro : FluxBase
{
    public override required string ModelName {get ;set;} = "black-forest-labs/flux-1.1-pro";

    [JsonPropertyName("width")]
    [Range(256, 1440, ErrorMessage = "Width must be between 256 and 1440.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    [Range(256, 1440, ErrorMessage = "Height must be between 256 and 1440.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Height { get; set; }
}