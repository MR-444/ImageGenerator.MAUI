using ImageGenerator.MAUI.Core.Domain.Ideogram;

namespace ImageGenerator.MAUI.Infrastructure.External.Mutation;

/// <summary>
/// The enrichment invariant gate: region enrichment may ONLY rewrite element <c>desc</c> fields. This
/// compares a candidate against the base and reports any drift the validator can't catch — a changed
/// element count, a moved/renamed/retyped element, a mutated bbox or palette, or a touched headline /
/// style / background. Pure and MAUI-free so the service can re-prompt on a violation and tests can assert
/// the rules directly.
/// </summary>
internal static class EnrichmentPreservation
{
    /// <summary>Returns human-readable violations; empty = only descs differ (or nothing did).</summary>
    public static IReadOnlyList<string> Check(V4JsonPrompt before, V4JsonPrompt after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var errors = new List<string>();

        if (!string.Equals(before.HighLevelDescription, after.HighLevelDescription, StringComparison.Ordinal))
            errors.Add("high_level_description was changed — it must be preserved exactly.");

        if (!StyleEqual(before.StyleDescription, after.StyleDescription))
            errors.Add("style_description was changed — it must be preserved exactly.");

        var beforeBg = before.CompositionalDeconstruction?.Background;
        var afterBg = after.CompositionalDeconstruction?.Background;
        if (!string.Equals(beforeBg, afterBg, StringComparison.Ordinal))
            errors.Add("background was changed — it must be preserved exactly.");

        var beforeElems = before.CompositionalDeconstruction?.Elements ?? [];
        var afterElems = after.CompositionalDeconstruction?.Elements ?? [];
        if (beforeElems.Count != afterElems.Count)
        {
            errors.Add($"element count changed from {beforeElems.Count} to {afterElems.Count} — keep every element.");
            return errors; // index-wise comparison below would be meaningless
        }

        for (var i = 0; i < beforeElems.Count; i++)
        {
            var b = beforeElems[i];
            var a = afterElems[i];
            if (!string.Equals(b.Type, a.Type, StringComparison.Ordinal))
                errors.Add($"element #{i} type changed ('{b.Type}' → '{a.Type}') — preserve it.");
            if (!string.Equals(b.Text, a.Text, StringComparison.Ordinal))
                errors.Add($"element #{i} text changed — preserve the literal text verbatim.");
            if (!BboxEqual(b.Bbox, a.Bbox))
                errors.Add($"element #{i} bbox changed — preserve the placement exactly.");
            if (!PaletteEqual(b.ColorPalette, a.ColorPalette))
                errors.Add($"element #{i} color_palette changed — preserve it.");
        }

        return errors;
    }

    private static bool StyleEqual(StyleDescription? b, StyleDescription? a)
    {
        if (b is null || a is null) return b is null && a is null;
        return string.Equals(b.Aesthetics, a.Aesthetics, StringComparison.Ordinal)
            && string.Equals(b.Lighting, a.Lighting, StringComparison.Ordinal)
            && string.Equals(b.Medium, a.Medium, StringComparison.Ordinal)
            && string.Equals(b.ArtStyle, a.ArtStyle, StringComparison.Ordinal)
            && string.Equals(b.Photo, a.Photo, StringComparison.Ordinal)
            && PaletteEqual(b.ColorPalette, a.ColorPalette);
    }

    private static bool BboxEqual(int[]? b, int[]? a)
    {
        if (b is null || a is null) return b is null && a is null;
        return b.AsSpan().SequenceEqual(a);
    }

    private static bool PaletteEqual(List<string>? b, List<string>? a)
    {
        if (b is null || a is null) return b is null && a is null;
        if (b.Count != a.Count) return false;
        for (var i = 0; i < b.Count; i++)
            if (!string.Equals(b[i], a[i], StringComparison.Ordinal)) return false;
        return true;
    }
}
