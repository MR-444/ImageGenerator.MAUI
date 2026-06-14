namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

/// <summary>
/// LOOK operator: blends the caption's current style (the precedence parent) with one random library
/// fragment. The current branch (<c>art_style</c> xor <c>photo</c>) and medium are kept; the fragment's
/// branch text is appended, <c>aesthetics</c>/<c>lighting</c> are token-unioned (current first), and the
/// palettes are merged accents-first to 16. Deterministic uniform crossover — unlike fighting image
/// references. Returns <c>null</c> if the base has no style block or the blend produces no real change.
/// </summary>
public sealed class BlendStyleOperator : ICaptionOperator
{
    public MutationAxis Axis => MutationAxis.Look;

    public string Name => "BlendStyle";

    public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context)
    {
        if (source.StyleDescription is null)
            return null;

        var candidates = context.Library.StyleFragments
            .Where(f => !StyleMath.SameStyle(f.Style, source.StyleDescription))
            .ToList();

        if (candidates.Count == 0)
            return null;

        var primary = source.StyleDescription;
        var secondary = candidates[rng.Next(candidates.Count)].Style;

        var blended = new StyleDescription
        {
            Aesthetics = StyleMath.UnionTokens(primary.Aesthetics, secondary.Aesthetics),
            Lighting = StyleMath.UnionTokens(primary.Lighting, secondary.Lighting),
            Medium = primary.Medium,
            // Keep the precedence parent's branch; append the fragment's text on the same branch only.
            ArtStyle = AppendBranch(primary.ArtStyle, secondary.ArtStyle),
            Photo = AppendBranch(primary.Photo, secondary.Photo),
            ColorPalette = StyleMath.MergePalettes(
                primary.ColorPalette, secondary.ColorPalette, StyleDescription.MaxPaletteColors)
        };

        var clone = CaptionClone.Clone(source);
        clone.StyleDescription = blended;

        if (StyleMath.SameStyle(clone.StyleDescription, source.StyleDescription))
            return null;

        return V4JsonPromptValidator.Validate(clone).Count == 0 ? clone : null;
    }

    // Appends the fragment's text to the current branch text. If the current branch is empty (the parent
    // is on the other branch), stays empty so the single-branch invariant holds.
    private static string? AppendBranch(string? primary, string? secondary)
    {
        if (string.IsNullOrWhiteSpace(primary))
            return null;
        if (string.IsNullOrWhiteSpace(secondary) || string.Equals(primary, secondary, StringComparison.Ordinal))
            return primary;
        return $"{primary}, {secondary}";
    }
}
