using System.Text.RegularExpressions;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Produces a short, human-readable summary of the SINGLE change a one-operator mutation made to a
/// caption, for the variant job-card "Δ …" line. Every mutated variant differs from the base by
/// exactly one operator, so the priority walk lands on that change. The label is OPERATOR-LED — it
/// opens with a plain-English action ("Style → anime", "Moved: lantern (left)", "Added: koi fish")
/// and, where useful, a distinguishing detail computed by a WORD-level diff (the changed words),
/// never a head-truncated slice of a long shared sentence. The operator name disambiguates the LOOK
/// cases (a style swap vs a blend vs an ornament add all surface differently in the caption).
/// </summary>
public static class CaptionDiff
{
    /// <summary>The single salient change of <paramref name="variant"/> from <paramref name="baseP"/>,
    /// framed by the <paramref name="operatorName"/> that produced it (e.g. "SwapStyle").</summary>
    public static string Describe(V4JsonPrompt baseP, V4JsonPrompt variant, string? operatorName = null)
    {
        ArgumentNullException.ThrowIfNull(baseP);
        ArgumentNullException.ThrowIfNull(variant);

        // 1) Style (LOOK: SwapStyle / BlendStyle).
        var (styleLabel, styleOld, styleNew) = StyleFieldDiff(baseP.StyleDescription, variant.StyleDescription);
        if (styleLabel == "palette") return "Style recolored";
        if (styleLabel is not null)
        {
            if (operatorName == "BlendStyle")
            {
                // Aggregate the new words across ALL style fields a blend touches (art_style/photo +
                // aesthetics + lighting), not just the first differing one — otherwise two blends that
                // differ only past the first few art_style words (or in aesthetics/lighting) read
                // identical even though they render differently.
                var added = BlendAddedWords(baseP.StyleDescription, variant.StyleDescription, 6);
                return added.Length > 0 ? $"Style blended (+ {added})" : "Style blended";
            }
            // A full swap may land the parent on the photo branch (art_style null); fall back to a
            // representative field so the label never reads "Style → (none)".
            var newGist = string.IsNullOrWhiteSpace(styleNew) ? Repr(variant.StyleDescription) : styleNew;
            return $"Style → {Gist(newGist, 4)}";
        }

        var baseCd = baseP.CompositionalDeconstruction;
        var varCd = variant.CompositionalDeconstruction;

        // 2) Background (SCENE).
        if (!StringEq(baseCd?.Background, varCd?.Background))
            return $"Background → {Gist(varCd?.Background, 4)}";

        var be = baseCd?.Elements ?? [];
        var ve = varCd?.Elements ?? [];

        // 3) Element added / removed.
        if (ve.Count > be.Count)
        {
            var added = FirstNotIn(ve, be);
            return added is null ? "Added an element" : $"Added: {ElementName(added)}";
        }
        if (ve.Count < be.Count)
        {
            var removed = FirstNotIn(be, ve);
            return removed is null ? "Removed an element" : $"Removed: {ElementName(removed)}";
        }

        // 4) Same count → the one element that changed (desc / text / bbox / palette).
        for (var i = 0; i < be.Count && i < ve.Count; i++)
        {
            var change = DescribeElement(be[i], ve[i], operatorName);
            if (change is not null) return change;
        }

        return FriendlyOperator(operatorName);
    }

    /// <summary>Plain-English fallback when the caption can't be re-parsed or the diff finds nothing.</summary>
    public static string FriendlyOperator(string? name) => name switch
    {
        "SwapStyle" => "Style changed",
        "BlendStyle" => "Style blended",
        "ApplyOrnamentKit" => "Ornament added",
        "MutateBbox" => "Moved an element",
        "MutatePalette" => "Recolored an element",
        "AddElement" => "Added an element",
        "RemoveElement" => "Removed an element",
        "SwapElementDesc" => "Reworded an element",
        _ => "Changed",
    };

    /// <summary>Present-tense "what it does" line for the page's read-only "Operators that will run" list.</summary>
    public static string OperatorBlurb(string? name) => name switch
    {
        "SwapStyle" => "Swap the art style",
        "BlendStyle" => "Blend in another style",
        "ApplyOrnamentKit" => "Add ornament detail",
        "MutateBbox" => "Move or resize an element",
        "MutatePalette" => "Recolor an element",
        "AddElement" => "Add a scene element",
        "RemoveElement" => "Remove a scene element",
        "SwapElementDesc" => "Reword an element",
        _ => name ?? string.Empty,
    };

    private static string? DescribeElement(Element b, Element v, string? operatorName)
    {
        if (!StringEq(b.Desc, v.Desc))
        {
            if (operatorName == "ApplyOrnamentKit")
            {
                var added = NewWords(b.Desc, v.Desc, 4);
                return added.Length > 0 ? $"Ornament added: {added}" : $"Ornament on {ElementName(b)}";
            }
            return $"Reworded: {ElementName(b)}";
        }
        if (!StringEq(b.Text, v.Text)) return $"Text → {Gist(v.Text, 4)}";
        if (!BboxEq(b.Bbox, v.Bbox)) return $"Moved: {ElementName(b)} ({BboxDir(b.Bbox, v.Bbox)})";
        if (!PaletteEq(b.ColorPalette, v.ColorPalette)) return $"Recolored: {ElementName(b)}";
        return null;
    }

    // ── Style ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The first differing style field (label, old, new); ("palette",…) when only the style
    /// palette moved; (null,…) when the blocks match.</summary>
    private static (string? label, string? oldV, string? newV) StyleFieldDiff(StyleDescription? a, StyleDescription? b)
    {
        if (a is null && b is null) return (null, null, null);
        if (a is null) return ("style", null, Repr(b!));
        if (b is null) return ("style", Repr(a), null);

        (string Label, string? A, string? B)[] fields =
        [
            ("art style", a.ArtStyle, b.ArtStyle),
            ("photo style", a.Photo, b.Photo),
            ("medium", a.Medium, b.Medium),
            ("aesthetics", a.Aesthetics, b.Aesthetics),
            ("lighting", a.Lighting, b.Lighting),
        ];
        foreach (var (label, av, bv) in fields)
            if (!StringEq(av, bv)) return (label, av, bv);

        return PaletteEq(a.ColorPalette, b.ColorPalette) ? (null, null, null) : ("palette", null, null);
    }

    private static string? Repr(StyleDescription? s) =>
        s?.ArtStyle ?? s?.Photo ?? s?.Medium ?? s?.Aesthetics ?? s?.Lighting;

    /// <summary>The fresh words a blend introduced, gathered across every word-bearing style field
    /// (the palette is hex codes, not words; medium is kept unchanged by the blend) so different
    /// blends produce different labels.</summary>
    private static string BlendAddedWords(StyleDescription? a, StyleDescription? b, int maxWords)
    {
        if (b is null) return string.Empty;
        var old = StyleTokens(a).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fresh = StyleTokens(b)
            .Where(t => !old.Contains(t) && !Stop.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxWords);
        return string.Join(' ', fresh);
    }

    private static IEnumerable<string> StyleTokens(StyleDescription? s)
    {
        if (s is null) yield break;
        foreach (var field in new[] { s.ArtStyle, s.Photo, s.Aesthetics, s.Lighting })
            foreach (var token in Tokens(field))
                yield return token;
    }

    // ── Bbox direction ─────────────────────────────────────────────────────────────────────────────

    /// <summary>A one-word move description (left/right/up/down, or bigger/smaller, or nudged).</summary>
    private static string BboxDir(int[]? oldB, int[]? newB)
    {
        if (oldB is null || newB is null || oldB.Length < 4 || newB.Length < 4) return "moved";
        var (oy, ox) = BboxMath.Center(oldB);
        var (ny, nx) = BboxMath.Center(newB);
        double dx = nx - ox, dy = ny - oy; // grid is [y_min,x_min,y_max,x_max]; +x = right, +y = down.
        double oldArea = (double)BboxMath.Width(oldB) * BboxMath.Height(oldB);
        double newArea = (double)BboxMath.Width(newB) * BboxMath.Height(newB);
        var ratio = oldArea > 0 ? newArea / oldArea : 1.0;

        const double moveThreshold = 15.0; // grid units (0–1000)
        if (Math.Abs(dx) > moveThreshold || Math.Abs(dy) > moveThreshold)
            return Math.Abs(dx) >= Math.Abs(dy) ? (dx > 0 ? "right" : "left") : (dy > 0 ? "down" : "up");
        if (ratio > 1.15) return "bigger";
        if (ratio < 0.87) return "smaller";
        return "nudged";
    }

    // ── Text helpers ────────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase) { "a", "an", "the" };

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    { "a", "an", "the", "with", "and", "of", "in", "on", "at", "to", "for", "by" };

    /// <summary>First <paramref name="maxWords"/> words of <paramref name="s"/> (one leading article
    /// dropped), with an ellipsis when more follow. "(none)" for empty.</summary>
    private static string Gist(string? s, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(none)";
        var words = s.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && Articles.Contains(words[0])) words.RemoveAt(0);
        var more = words.Count > maxWords;
        var text = string.Join(' ', words.Take(maxWords));
        return more ? text + "…" : text;
    }

    /// <summary>The words present in <paramref name="newText"/> but not <paramref name="oldText"/>
    /// (stop-words dropped), up to <paramref name="maxWords"/> — the gist of what a blend/ornament added.</summary>
    private static string NewWords(string? oldText, string? newText, int maxWords)
    {
        var oldSet = Tokens(oldText).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fresh = Tokens(newText)
            .Where(t => !oldSet.Contains(t) && !Stop.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxWords);
        return string.Join(' ', fresh);
    }

    private static IEnumerable<string> Tokens(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? []
            : Regex.Split(s, @"[^\p{L}\p{Nd}]+").Where(w => w.Length > 0);

    private static string ElementName(Element e) =>
        Gist(e.Type == Element.TextType && !string.IsNullOrWhiteSpace(e.Text) ? e.Text : e.Desc, 3);

    // ── Equality ────────────────────────────────────────────────────────────────────────────────────

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
}
