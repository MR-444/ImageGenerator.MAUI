namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

/// <summary>
/// LOOK operator: injects a named ornament kit's phrases into elements by their resolved slot tag,
/// keeping every desc within the 60-word budget via <see cref="DescBudget"/>. Composition and style are
/// untouched. Returns <c>null</c> when the library has no kit, or the chosen kit changes nothing.
/// </summary>
public sealed class ApplyOrnamentKitOperator : ICaptionOperator
{
    public MutationAxis Axis => MutationAxis.Look;

    public string Name => "ApplyOrnamentKit";

    public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context)
    {
        var kits = context.Library.OrnamentKits;
        if (kits.Count == 0)
            return null;

        var kit = kits[rng.Next(kits.Count)];

        var clone = CaptionClone.Clone(source);
        var sourceElements = source.CompositionalDeconstruction.Elements;
        var cloneElements = clone.CompositionalDeconstruction.Elements;

        var changed = false;
        // Tags are keyed by the SOURCE elements; the clone has parallel elements by index.
        for (var i = 0; i < sourceElements.Count; i++)
        {
            if (!context.Tags.TryGetValue(sourceElements[i], out var tag))
                continue;
            if (!kit.PhrasesBySlot.TryGetValue(tag, out var phrases) || phrases.Count == 0)
                continue;

            var fitted = DescBudget.Fit(cloneElements[i].Desc ?? string.Empty, phrases);
            if (fitted is null || string.Equals(fitted, cloneElements[i].Desc, StringComparison.Ordinal))
                continue;

            cloneElements[i].Desc = fitted;
            changed = true;
        }

        if (!changed)
            return null;

        return V4JsonPromptValidator.Validate(clone).Count == 0 ? clone : null;
    }
}
