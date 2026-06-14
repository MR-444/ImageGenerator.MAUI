namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Produces a short, human-readable summary of the SINGLE structural change a one-operator
/// mutation made to a caption. Every mutated variant differs from the base by exactly one operator,
/// so the priority walk below lands on that change. Used to label variant job cards ("art style:
/// gouache → anime", "placement moved") so the user can see at a glance what each variant tried.
/// </summary>
public static class CaptionDiff
{
    private const int MaxFieldLen = 30;

    /// <summary>The first salient difference of <paramref name="variant"/> from <paramref name="baseP"/>.</summary>
    public static string Describe(V4JsonPrompt baseP, V4JsonPrompt variant)
    {
        ArgumentNullException.ThrowIfNull(baseP);
        ArgumentNullException.ThrowIfNull(variant);

        // 1) Style (LOOK axis: SwapStyle / BlendStyle).
        var style = DescribeStyle(baseP.StyleDescription, variant.StyleDescription);
        if (style is not null) return style;

        var baseCd = baseP.CompositionalDeconstruction;
        var varCd = variant.CompositionalDeconstruction;

        // 2) Background (SCENE).
        if (!StringEq(baseCd?.Background, varCd?.Background)) return "background changed";

        var be = baseCd?.Elements ?? [];
        var ve = varCd?.Elements ?? [];

        // 3) Element added / removed.
        if (ve.Count > be.Count)
        {
            var added = FirstNotIn(ve, be);
            return added is null ? "added an element" : $"added: {ShortBody(added)}";
        }
        if (ve.Count < be.Count)
        {
            var removed = FirstNotIn(be, ve);
            return removed is null ? "removed an element" : $"removed: {ShortBody(removed)}";
        }

        // 4) Same count → the one element that changed (desc / text / bbox / palette).
        for (var i = 0; i < be.Count && i < ve.Count; i++)
        {
            var change = DescribeElement(be[i], ve[i]);
            if (change is not null) return change;
        }

        return "changed";
    }

    private static string? DescribeStyle(StyleDescription? a, StyleDescription? b)
    {
        if (a is null && b is null) return null;
        if (a is null) return "style added";
        if (b is null) return "style removed";

        var field = FieldDiff("art style", a.ArtStyle, b.ArtStyle)
                 ?? FieldDiff("photo style", a.Photo, b.Photo)
                 ?? FieldDiff("medium", a.Medium, b.Medium)
                 ?? FieldDiff("aesthetics", a.Aesthetics, b.Aesthetics)
                 ?? FieldDiff("lighting", a.Lighting, b.Lighting);
        if (field is not null) return field;

        return PaletteEq(a.ColorPalette, b.ColorPalette) ? null : "style palette changed";
    }

    private static string? FieldDiff(string label, string? a, string? b) =>
        StringEq(a, b) ? null : $"{label}: {Trunc(a)} → {Trunc(b)}";

    private static string? DescribeElement(Element b, Element v)
    {
        if (!StringEq(b.Desc, v.Desc)) return $"desc: {Trunc(b.Desc)} → {Trunc(v.Desc)}";
        if (!StringEq(b.Text, v.Text)) return $"text: {Trunc(b.Text)} → {Trunc(v.Text)}";
        if (!BboxEq(b.Bbox, v.Bbox)) return "placement moved";
        if (!PaletteEq(b.ColorPalette, v.ColorPalette)) return "element palette changed";
        return null;
    }

    /// <summary>An element of <paramref name="list"/> (matched by desc/text) absent from <paramref name="other"/>.</summary>
    private static Element? FirstNotIn(IReadOnlyList<Element> list, IReadOnlyList<Element> other)
    {
        foreach (var e in list)
        {
            var key = Key(e);
            if (!other.Any(o => string.Equals(Key(o), key, StringComparison.Ordinal))) return e;
        }
        return null;
    }

    private static string Key(Element e) => e.Desc ?? e.Text ?? string.Empty;

    private static string ShortBody(Element e) =>
        Trunc(e.Type == Element.TextType && !string.IsNullOrWhiteSpace(e.Text) ? e.Text : e.Desc);

    private static bool StringEq(string? a, string? b) =>
        string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);

    private static bool BboxEq(int[]? a, int[]? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.AsSpan().SequenceEqual(b);
    }

    private static bool PaletteEq(List<string>? a, List<string>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.SequenceEqual(b, StringComparer.Ordinal);
    }

    private static string Trunc(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(none)";
        s = s.Trim();
        return s.Length <= MaxFieldLen ? s : s[..MaxFieldLen] + "…";
    }
}
