namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Shared, deterministic operations on <see cref="StyleDescription"/> used by the LOOK operators:
/// value equality (to "exclude current" on a swap), deep clone, comma-token union, and palette merge.
/// </summary>
public static class StyleMath
{
    /// <summary>Field-by-field value equality, including palette sequence (ordinal).</summary>
    public static bool SameStyle(StyleDescription? a, StyleDescription? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        return a.Aesthetics == b.Aesthetics
            && a.Lighting == b.Lighting
            && a.Medium == b.Medium
            && a.ArtStyle == b.ArtStyle
            && a.Photo == b.Photo
            && PaletteEquals(a.ColorPalette, b.ColorPalette);
    }

    private static bool PaletteEquals(List<string>? a, List<string>? b)
    {
        var left = a ?? [];
        var right = b ?? [];
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    /// <summary>Independent deep copy (palette list copied) so operators never alias library data.</summary>
    public static StyleDescription Clone(StyleDescription style) => new()
    {
        Aesthetics = style.Aesthetics,
        Lighting = style.Lighting,
        Medium = style.Medium,
        ArtStyle = style.ArtStyle,
        Photo = style.Photo,
        ColorPalette = style.ColorPalette is null ? null : [.. style.ColorPalette]
    };

    /// <summary>
    /// Comma-separated token union: <paramref name="primary"/> tokens first, then any from
    /// <paramref name="secondary"/> not already present (case-insensitive), order-stable. Null/blank
    /// inputs are treated as empty; returns null only when both are empty.
    /// </summary>
    public static string? UnionTokens(string? primary, string? secondary)
    {
        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in SplitTokens(primary).Concat(SplitTokens(secondary)))
        {
            if (seen.Add(token))
                tokens.Add(token);
        }

        return tokens.Count == 0 ? null : string.Join(", ", tokens);
    }

    private static IEnumerable<string> SplitTokens(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Union of two palettes (primary first), deduped ordinally, ordered so the most saturated "accent"
    /// colors survive, then trimmed to <paramref name="maxColors"/>. Returns null when both are empty.
    /// </summary>
    public static List<string>? MergePalettes(List<string>? primary, List<string>? secondary, int maxColors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var combined = new List<string>();
        foreach (var color in (primary ?? []).Concat(secondary ?? []))
        {
            if (seen.Add(color))
                combined.Add(color);
        }

        if (combined.Count == 0)
            return null;

        return combined
            .OrderByDescending(ColorMath.Saturation)   // OrderBy is stable: ties keep union order
            .Take(maxColors)
            .ToList();
    }
}
