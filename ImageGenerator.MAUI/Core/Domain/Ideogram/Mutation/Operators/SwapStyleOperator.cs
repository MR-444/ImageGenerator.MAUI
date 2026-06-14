using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

/// <summary>
/// LOOK operator: replaces the whole <c>style_description</c> with a named library fragment, chosen
/// uniformly at random from the library excluding the current style (so the result is guaranteed to
/// differ). Composition is untouched. Returns <c>null</c> when no differing fragment is available.
/// </summary>
public sealed class SwapStyleOperator : ICaptionOperator
{
    public MutationAxis Axis => MutationAxis.Look;

    public string Name => "SwapStyle";

    public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context)
    {
        var candidates = context.Library.StyleFragments
            .Where(f => !StyleMath.SameStyle(f.Style, source.StyleDescription))
            .ToList();

        if (candidates.Count == 0)
            return null;

        var fragment = candidates[rng.Next(candidates.Count)];

        var clone = CaptionClone.Clone(source);
        clone.StyleDescription = StyleMath.Clone(fragment.Style);

        return V4JsonPromptValidator.Validate(clone).Count == 0 ? clone : null;
    }
}
