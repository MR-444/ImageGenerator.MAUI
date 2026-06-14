namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

/// <summary>
/// SCENE operator: removes one standalone scene element (a prop or scene-fill — never the subject, its
/// garment/props, or required text). Keeps at least <see cref="MinElements"/> elements so the subject and at
/// least one companion always survive. Returns <c>null</c> when nothing is safe to remove or the floor is
/// reached.
/// </summary>
public sealed class RemoveElementOperator : ICaptionOperator
{
    private const int MinElements = 2;

    public MutationAxis Axis => MutationAxis.Scene;

    public string Name => "RemoveElement";

    public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context)
    {
        var sourceElements = source.CompositionalDeconstruction.Elements;
        if (sourceElements.Count <= MinElements)
            return null;

        var candidates = new List<int>();
        for (var i = 0; i < sourceElements.Count; i++)
        {
            if (IsRemovable(context, sourceElements[i]))
                candidates.Add(i);
        }

        if (candidates.Count == 0)
            return null;

        var index = candidates[rng.Next(candidates.Count)];

        var clone = CaptionClone.Clone(source);
        clone.CompositionalDeconstruction.Elements.RemoveAt(index);

        return V4JsonPromptValidator.Validate(clone).Count == 0 ? clone : null;
    }

    // Removable = a standalone scene prop or untagged obj. Never a subject.* part (incoherent without the
    // subject) and never a text.* element (don't silently drop required copy).
    private static bool IsRemovable(MutationContext context, Element element)
    {
        if (element.Type == Element.TextType)
            return false;

        if (!context.Tags.TryGetValue(element, out var tag))
            return true;                            // untagged obj — a plain scene object, safe to drop

        return !tag.StartsWith("subject.", StringComparison.Ordinal)
            && !tag.StartsWith("text.", StringComparison.Ordinal);
    }
}
