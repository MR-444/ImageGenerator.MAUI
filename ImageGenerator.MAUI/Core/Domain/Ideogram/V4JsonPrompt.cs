using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram;

/// <summary>
/// Typed model of Ideogram V4's structured <c>json_prompt</c>. One instance backs both
/// serializations: compact string into <c>Parameters.Prompt</c> (Replicate's cog types the
/// field as a JSON <em>string</em>, not an object — a live 422 proved it) and pretty-printed
/// <c>.json</c> on disk for use outside this app.
/// Property declaration order is the serialization order — keep it matching the schema docs.
/// </summary>
public sealed class V4JsonPrompt
{
    /// <summary>Bbox coordinates live on a fixed 1000x1000 grid regardless of output resolution.</summary>
    public const int CanvasSize = 1000;

    [JsonPropertyName("high_level_description")]
    public string HighLevelDescription { get; set; } = string.Empty;

    [JsonPropertyName("style_description")]
    public StyleDescription? StyleDescription { get; set; }

    [JsonPropertyName("compositional_deconstruction")]
    public CompositionalDeconstruction CompositionalDeconstruction { get; set; } = new();
}

/// <summary>
/// Optional aesthetics block. <c>art_style</c> (non-photographic) and <c>photo</c>
/// (photographic style) are mutually exclusive — the validator enforces exactly one.
/// </summary>
public sealed class StyleDescription
{
    public const int MaxPaletteColors = 16;

    [JsonPropertyName("aesthetics")]
    public string? Aesthetics { get; set; }

    [JsonPropertyName("lighting")]
    public string? Lighting { get; set; }

    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    [JsonPropertyName("art_style")]
    public string? ArtStyle { get; set; }

    [JsonPropertyName("photo")]
    public string? Photo { get; set; }

    [JsonPropertyName("color_palette")]
    public List<string>? ColorPalette { get; set; }
}

public sealed class CompositionalDeconstruction
{
    [JsonPropertyName("background")]
    public string Background { get; set; } = string.Empty;

    [JsonPropertyName("elements")]
    public List<Element> Elements { get; set; } = [];
}

/// <summary>
/// One placed element. Discriminated by <see cref="Type"/>: <c>obj</c> carries only a
/// description, <c>text</c> additionally carries the literal text to render. Modeled as a
/// single mutable class (not a hierarchy) so the editor can retype an element in place and
/// bind it from an ObservableCollection without copying.
/// </summary>
public sealed class Element
{
    public const string ObjType = "obj";
    public const string TextType = "text";
    public const int MaxPaletteColors = 5;

    [JsonPropertyName("type")]
    public string Type { get; set; } = ObjType;

    /// <summary>[y_min, x_min, y_max, x_max] on the 0–1000 grid, origin top-left. Null = unplaced.</summary>
    [JsonPropertyName("bbox")]
    public int[]? Bbox { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("desc")]
    public string? Desc { get; set; }

    [JsonPropertyName("color_palette")]
    public List<string>? ColorPalette { get; set; }
}
