namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

/// <summary>
/// SCENE operator: perturbs one non-identity element's bbox by a Gaussian step sized to
/// <see cref="MutationContext.Strength"/> — translate, scale (about center), or per-corner jitter, one per
/// variant. Aspect-ratio aware (round objects stay round, single subjects stay narrow on wide frames) and
/// guards against overlaps that would risk a duplicate-subject artifact. The identity element is never moved
/// or scaled. Returns <c>null</c> when no legal, changed, non-degenerate box can be produced.
/// </summary>
public sealed class MutateBboxOperator : ICaptionOperator
{
    private const int MinSpan = 15;                          // ~1.5% of the 1000 grid — reject collapsed boxes
    private const double SubjectOverlapThreshold = 0.6;
    private const double MinScaleFactor = 0.6;
    private const double MaxScaleFactor = 1.4;
    private const double AspectNudge = 0.25;                 // fraction of the way toward the AR target per scale

    public MutationAxis Axis => MutationAxis.Scene;

    public string Name => "MutateBbox";

    public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context)
    {
        var sourceElements = source.CompositionalDeconstruction.Elements;

        // Candidate = has a real box AND is not the identity element (its scale is protected).
        var candidates = new List<int>();
        for (var i = 0; i < sourceElements.Count; i++)
        {
            if (sourceElements[i].Bbox is not { Length: 4 })
                continue;
            if (Tag(context, sourceElements[i]) == SlotTag.Subject.Identity)
                continue;
            candidates.Add(i);
        }

        if (candidates.Count == 0)
            return null;

        var index = candidates[rng.Next(candidates.Count)];
        var original = sourceElements[index].Bbox!;
        var sigma = BboxMath.SigmaFor(context.Strength);
        var single = IsSubject(Tag(context, sourceElements[index]));

        var moved = rng.Next(3) switch
        {
            0 => TranslateBox(original, rng, sigma, single, context),
            1 => ScaleBox(original, rng, sigma, context),
            _ => JitterBox(original, rng, sigma)
        };

        var repaired = V4JsonPromptValidator.ClampBbox(moved);

        if (repaired.SequenceEqual(original))
            return null;
        if (BboxMath.IsDegenerate(repaired, MinSpan))
            return null;
        if (RisksDuplicateSubject(context, sourceElements, index, repaired))
            return null;

        var clone = CaptionClone.Clone(source);
        clone.CompositionalDeconstruction.Elements[index].Bbox = repaired;

        return V4JsonPromptValidator.Validate(clone).Count == 0 ? clone : null;
    }

    private static int[] TranslateBox(int[] box, Random rng, double sigma, bool single, MutationContext context)
    {
        var dy = (int)Math.Round(BboxMath.NextGaussian(rng, sigma));
        var dx = BboxMath.NextGaussian(rng, sigma);
        // A single subject drifts less horizontally on a wide frame (keeps it roughly centered).
        if (single)
            dx *= BboxMath.GridAspectForSquareObject(context.TargetWidth, context.TargetHeight);
        return BboxMath.Translate(box, dy, (int)Math.Round(dx));
    }

    private static int[] ScaleBox(int[] box, Random rng, double sigma, MutationContext context)
    {
        var sy = Math.Clamp(1.0 + BboxMath.NextGaussian(rng, sigma) / V4JsonPrompt.CanvasSize, MinScaleFactor, MaxScaleFactor);
        var sx = Math.Clamp(1.0 + BboxMath.NextGaussian(rng, sigma) / V4JsonPrompt.CanvasSize, MinScaleFactor, MaxScaleFactor);
        var scaled = BboxMath.Scale(box, sy, sx);

        // Nudge the box aspect a fraction toward the AR target so round objects stay round.
        var target = BboxMath.GridAspectForSquareObject(context.TargetWidth, context.TargetHeight);
        var height = BboxMath.Height(scaled);
        if (height > 0)
        {
            var current = (double)BboxMath.Width(scaled) / height;
            if (current > 0)
            {
                var desired = current + (target - current) * AspectNudge;
                scaled = BboxMath.Scale(scaled, 1.0, desired / current);
            }
        }

        return scaled;
    }

    private static int[] JitterBox(int[] box, Random rng, double sigma)
    {
        var s = sigma * 0.5;
        return
        [
            box[0] + (int)Math.Round(BboxMath.NextGaussian(rng, s)),
            box[1] + (int)Math.Round(BboxMath.NextGaussian(rng, s)),
            box[2] + (int)Math.Round(BboxMath.NextGaussian(rng, s)),
            box[3] + (int)Math.Round(BboxMath.NextGaussian(rng, s))
        ];
    }

    // Flags overlaps that risk a duplicate subject: only when the moved box OR the box it lands on belongs
    // to a subject.* element. Two non-subject elements overlapping (e.g. two flowers) is allowed.
    private static bool RisksDuplicateSubject(
        MutationContext context, IReadOnlyList<Element> elements, int movedIndex, int[] moved)
    {
        var movedIsSubject = IsSubject(Tag(context, elements[movedIndex]));
        for (var i = 0; i < elements.Count; i++)
        {
            if (i == movedIndex || elements[i].Bbox is not { Length: 4 } other)
                continue;
            if (!movedIsSubject && !IsSubject(Tag(context, elements[i])))
                continue;
            if (BboxMath.IoU(moved, other) > SubjectOverlapThreshold)
                return true;
        }

        return false;
    }

    private static string? Tag(MutationContext context, Element element) =>
        context.Tags.TryGetValue(element, out var tag) ? tag : null;

    private static bool IsSubject(string? tag) =>
        tag is not null && tag.StartsWith("subject.", StringComparison.Ordinal);
}
