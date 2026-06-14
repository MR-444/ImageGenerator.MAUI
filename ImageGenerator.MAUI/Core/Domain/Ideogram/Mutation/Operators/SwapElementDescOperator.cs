using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

/// <summary>
/// SCENE operator: replaces one element's description with an alternate from the library's scene-element pool
/// that shares its slot tag, keeping the new desc within the 60-word budget. The subject identity is never
/// rewritten. Returns <c>null</c> when no element has a differing, same-slot, budget-legal alternate.
/// </summary>
public sealed class SwapElementDescOperator : ICaptionOperator
{
    public MutationAxis Axis => MutationAxis.Scene;

    public string Name => "SwapElementDesc";

    public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context)
    {
        var sourceElements = source.CompositionalDeconstruction.Elements;

        // Element indices that have at least one differing, same-slot, budget-legal alternate.
        var candidates = new List<(int Index, List<SceneElement> Alternates)>();
        for (var i = 0; i < sourceElements.Count; i++)
        {
            if (!context.Tags.TryGetValue(sourceElements[i], out var tag))
                continue;
            if (tag == SlotTag.Subject.Identity)
                continue;

            var current = sourceElements[i].Desc;
            var alternates = context.Library.SceneElements
                .Where(t => t.SlotTag == tag
                    && !string.Equals(t.Desc, current, StringComparison.Ordinal)
                    && DescBudget.CountWords(t.Desc) <= DescBudget.MaxWords)
                .ToList();

            if (alternates.Count > 0)
                candidates.Add((i, alternates));
        }

        if (candidates.Count == 0)
            return null;

        var (index, picks) = candidates[rng.Next(candidates.Count)];
        var alternate = picks[rng.Next(picks.Count)];

        var clone = CaptionClone.Clone(source);
        clone.CompositionalDeconstruction.Elements[index].Desc = alternate.Desc;

        return V4JsonPromptValidator.Validate(clone).Count == 0 ? clone : null;
    }
}
