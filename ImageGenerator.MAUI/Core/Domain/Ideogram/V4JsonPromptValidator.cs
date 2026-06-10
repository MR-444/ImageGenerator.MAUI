using System.Text.RegularExpressions;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram;

/// <summary>
/// Pre-flight checks for a <see cref="V4JsonPrompt"/> before it is serialized into the prompt
/// box or exported to disk. Returns human-readable errors for the editor's status surface —
/// never throws on bad content (only <see cref="ClampBbox"/> rejects a structurally impossible
/// argument).
/// </summary>
public static partial class V4JsonPromptValidator
{
    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexColorRegex();

    public static IReadOnlyList<string> Validate(V4JsonPrompt model)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(model.HighLevelDescription))
            errors.Add("High-level description is required.");

        ValidateStyle(model.StyleDescription, errors);

        var composition = model.CompositionalDeconstruction;
        if (composition is null)
        {
            errors.Add("Compositional deconstruction is required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(composition.Background))
            errors.Add("Background description is required.");

        for (var i = 0; i < composition.Elements.Count; i++)
            ValidateElement(composition.Elements[i], i + 1, errors);

        return errors;
    }

    private static void ValidateStyle(StyleDescription? style, List<string> errors)
    {
        if (style is null) return;

        if (string.IsNullOrWhiteSpace(style.Medium))
            errors.Add("Style: medium is required when a style description is included.");

        var hasArtStyle = !string.IsNullOrWhiteSpace(style.ArtStyle);
        var hasPhoto = !string.IsNullOrWhiteSpace(style.Photo);
        if (hasArtStyle == hasPhoto)
            errors.Add(hasArtStyle
                ? "Style: art_style and photo are mutually exclusive — set only one."
                : "Style: set either art_style (non-photographic) or photo (photographic).");

        ValidatePalette(style.ColorPalette, StyleDescription.MaxPaletteColors, "Style palette", errors);
    }

    private static void ValidateElement(Element element, int position, List<string> errors)
    {
        var label = $"Element {position} ({element.Type})";

        if (element.Type is not (Element.ObjType or Element.TextType))
            errors.Add($"Element {position}: type must be '{Element.ObjType}' or '{Element.TextType}'.");

        if (string.IsNullOrWhiteSpace(element.Desc))
            errors.Add($"{label}: description is required.");

        if (element.Type == Element.TextType && string.IsNullOrWhiteSpace(element.Text))
            errors.Add($"{label}: text content is required.");

        if (element.Bbox is { } bbox)
        {
            if (bbox.Length != 4)
                errors.Add($"{label}: bbox must have exactly 4 values [y_min, x_min, y_max, x_max].");
            else
            {
                if (bbox.Any(v => v is < 0 or > V4JsonPrompt.CanvasSize))
                    errors.Add($"{label}: bbox values must be between 0 and {V4JsonPrompt.CanvasSize}.");
                if (bbox[0] > bbox[2] || bbox[1] > bbox[3])
                    errors.Add($"{label}: bbox minimums must not exceed maximums (y_min ≤ y_max, x_min ≤ x_max).");
            }
        }

        ValidatePalette(element.ColorPalette, Element.MaxPaletteColors, $"{label} palette", errors);
    }

    private static void ValidatePalette(List<string>? palette, int maxColors, string label, List<string> errors)
    {
        if (palette is null || palette.Count == 0) return;

        if (palette.Count > maxColors)
            errors.Add($"{label}: at most {maxColors} colors allowed (got {palette.Count}).");

        foreach (var color in palette.Where(c => !HexColorRegex().IsMatch(c)))
            errors.Add($"{label}: '{color}' is not an uppercase #RRGGBB hex color.");
    }

    /// <summary>
    /// Returns a schema-legal copy of <paramref name="bbox"/>: each value clamped to the
    /// 0–1000 grid and corners reordered so min ≤ max on both axes.
    /// </summary>
    public static int[] ClampBbox(int[] bbox)
    {
        ArgumentNullException.ThrowIfNull(bbox);
        if (bbox.Length != 4)
            throw new ArgumentException($"bbox must have exactly 4 values, got {bbox.Length}.", nameof(bbox));

        var yA = Math.Clamp(bbox[0], 0, V4JsonPrompt.CanvasSize);
        var xA = Math.Clamp(bbox[1], 0, V4JsonPrompt.CanvasSize);
        var yB = Math.Clamp(bbox[2], 0, V4JsonPrompt.CanvasSize);
        var xB = Math.Clamp(bbox[3], 0, V4JsonPrompt.CanvasSize);

        return [Math.Min(yA, yB), Math.Min(xA, xB), Math.Max(yA, yB), Math.Max(xA, xB)];
    }
}
