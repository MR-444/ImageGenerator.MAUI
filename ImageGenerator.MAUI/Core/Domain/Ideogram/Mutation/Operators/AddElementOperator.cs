namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

/// <summary>
/// SCENE operator: inserts one new element drawn from the library's scene-element pool, placed at its
/// preferred box (jittered by <see cref="MutationContext.Strength"/>). Never adds a second subject, and
/// rejects placements that overlap the subject enough to risk a duplicate-subject artifact. Returns
/// <c>null</c> when the pool is empty or no safe placement validates.
/// </summary>
public sealed class AddElementOperator : ICaptionOperator
{
    private const double SubjectOverlapThreshold = 0.4;

    public MutationAxis Axis => MutationAxis.Scene;

    public string Name => "AddElement";

    public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context)
    {
        var templates = context.Library.SceneElements
            .Where(t => !t.SlotTag.StartsWith("subject.", StringComparison.Ordinal))
            .ToList();

        if (templates.Count == 0)
            return null;

        var template = templates[rng.Next(templates.Count)];

        var element = new Element
        {
            Type = template.Type,
            Desc = template.Desc,
            Text = template.Type == Element.TextType ? template.Text : null,
            ColorPalette = template.ColorPalette is null ? null : [.. template.ColorPalette],
            SlotTag = template.SlotTag
        };

        if (template.PreferredBbox is { Length: 4 } preferred)
        {
            var sigma = BboxMath.SigmaFor(context.Strength);
            var jittered = new[]
            {
                preferred[0] + (int)Math.Round(BboxMath.NextGaussian(rng, sigma)),
                preferred[1] + (int)Math.Round(BboxMath.NextGaussian(rng, sigma)),
                preferred[2] + (int)Math.Round(BboxMath.NextGaussian(rng, sigma)),
                preferred[3] + (int)Math.Round(BboxMath.NextGaussian(rng, sigma))
            };
            element.Bbox = V4JsonPromptValidator.ClampBbox(jittered);

            if (OverlapsSubject(context, source.CompositionalDeconstruction.Elements, element.Bbox))
                return null;
        }

        var clone = CaptionClone.Clone(source);
        clone.CompositionalDeconstruction.Elements.Add(element);

        return V4JsonPromptValidator.Validate(clone).Count == 0 ? clone : null;
    }

    private static bool OverlapsSubject(MutationContext context, IReadOnlyList<Element> elements, int[] box)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i].Bbox is not { Length: 4 } other)
                continue;
            if (!context.Tags.TryGetValue(elements[i], out var tag) ||
                !tag.StartsWith("subject.", StringComparison.Ordinal))
                continue;
            if (BboxMath.IoU(box, other) > SubjectOverlapThreshold)
                return true;
        }

        return false;
    }
}
