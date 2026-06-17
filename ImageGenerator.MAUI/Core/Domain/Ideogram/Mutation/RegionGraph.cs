namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>Coarse horizontal third a box's center falls in.</summary>
public enum HorizontalZone { Left, Center, Right }

/// <summary>Coarse vertical third a box's center falls in. Sky = upper (small y), Ground = lower (large y).</summary>
public enum VerticalBand { Sky, Middle, Ground }

/// <summary>Position of a relation's <c>From</c> element relative to its <c>To</c> element.</summary>
public enum RelativePosition { LeftOf, RightOf, Above, Below, Aligned }

/// <summary>How a relation's <c>From</c> element makes contact with its <c>To</c> element.</summary>
public enum SupportRelation { None, RestingOn, LeaningAgainst, Mounted }

/// <summary>
/// SOFT occlusion hint for an overlapping pair — never a verdict. <see cref="LikelyNearer"/> means the
/// <c>From</c> element is larger AND lower in frame than <c>To</c> (a weak "closer to camera" signal the
/// LLM must weigh against each desc's own semantics); <see cref="Ambiguous"/> means geometry can't tell.
/// </summary>
public enum DepthCue { LikelyNearer, LikelyFarther, Ambiguous }

/// <summary>
/// Per-element spatial facts derived from a bbox. All spatial fields are null when <see cref="IsPlaced"/>
/// is false (no/degenerate bbox). Fractions are 0–1 of the canvas; y grows downward.
/// </summary>
public sealed record ElementRegionFact(
    int Index,
    bool IsPlaced,
    HorizontalZone? Horizontal,
    VerticalBand? Band,
    bool SpansMultipleBands,
    double? CenterXFraction,
    double? CenterYFraction,
    double? AreaFraction,
    double? BottomYFraction);

/// <summary>
/// One pairwise spatial relation between two PLACED elements. <see cref="FromDepthCue"/> is non-null ONLY
/// when <see cref="Overlaps"/> is true. <see cref="FromIndex"/>/<see cref="ToIndex"/> reflect the support
/// direction when there is contact (<c>From</c> rests on/leans on/is mounted on <c>To</c>); otherwise the
/// lower index is <c>From</c>.
/// </summary>
public sealed record RegionRelation(
    int FromIndex,
    int ToIndex,
    RelativePosition Position,
    SupportRelation Support,
    bool Overlaps,
    double Iou,
    DepthCue? FromDepthCue);

/// <summary>The full spatial reading of a caption: a fact per element plus pruned pairwise relations.</summary>
public sealed record RegionGraphResult(
    IReadOnlyList<ElementRegionFact> Elements,
    IReadOnlyList<RegionRelation> Relations);

/// <summary>
/// Deterministic, MAUI-free spatial analysis of a V4 caption's element bboxes. Emits only facts geometry
/// can prove — relative position, support/contact, background band — plus, for overlapping pairs, a SOFT
/// depth cue. It deliberately NEVER asserts front/behind: element list order is a technical z-order, not
/// scene depth, so the actual occlusion call is left to the LLM (which also reads each desc). Bboxes are
/// <c>[y_min, x_min, y_max, x_max]</c> on the 0–1000 grid (<see cref="V4JsonPrompt.CanvasSize"/>), origin
/// top-left. Reuses <see cref="BboxMath"/>.
/// </summary>
public static class RegionGraph
{
    /// <summary>Half-width of the center dead-zone (grid units / 1000) for zone + Aligned classification.</summary>
    public const double CenterBandFraction = 0.10;

    /// <summary>Smallest box edge (grid units) that still counts as placed (mirrors the editor's MinBoxSize).</summary>
    public const int MinPlacedSpan = 20;

    /// <summary>Edge-contact tolerance (grid units / 1000) for support/contact detection.</summary>
    public const double SupportBandFraction = 0.06;

    /// <summary>Minimum shared-edge overlap ratio (of the smaller span) for a support relation.</summary>
    public const double SupportOverlapMin = 0.30;

    /// <summary>How much larger (area ratio) one box must be than the other to lean "nearer".</summary>
    public const double NearerAreaRatio = 1.30;

    /// <summary>How much lower (bottom-edge fraction) it must also sit to lean "nearer".</summary>
    public const double NearerBottomDelta = 0.05;

    private const double Canvas = V4JsonPrompt.CanvasSize;
    private static readonly double BandLow = Canvas / 3.0;        // Sky | Middle boundary
    private static readonly double BandHigh = Canvas * 2.0 / 3.0; // Middle | Ground boundary

    /// <summary>Compute the region graph for a caption's elements (null caption → empty result).</summary>
    public static RegionGraphResult Compute(V4JsonPrompt? prompt) =>
        Compute(prompt?.CompositionalDeconstruction?.Elements ?? []);

    /// <summary>Test/seam overload: compute directly from an element list.</summary>
    public static RegionGraphResult Compute(IReadOnlyList<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var facts = new List<ElementRegionFact>(elements.Count);
        var placed = new List<int>();
        for (var i = 0; i < elements.Count; i++)
        {
            var bbox = elements[i].Bbox;
            if (IsPlaced(bbox))
            {
                facts.Add(BuildFact(i, bbox!));
                placed.Add(i);
            }
            else
            {
                facts.Add(new ElementRegionFact(i, false, null, null, false, null, null, null, null));
            }
        }

        var relations = BuildRelations(elements, placed);
        return new RegionGraphResult(facts, relations);
    }

    private static bool IsPlaced(int[]? bbox) =>
        bbox is { Length: 4 } && !BboxMath.IsDegenerate(bbox, MinPlacedSpan);

    private static ElementRegionFact BuildFact(int index, int[] bbox)
    {
        var (cy, cx) = BboxMath.Center(bbox);
        var area = (double)BboxMath.Width(bbox) * BboxMath.Height(bbox) / (Canvas * Canvas);
        return new ElementRegionFact(
            index,
            IsPlaced: true,
            Horizontal: HorizontalOf(cx),
            Band: BandOf(cy),
            SpansMultipleBands: BandOfEdge(bbox[0]) != BandOfEdge(bbox[2]),
            CenterXFraction: cx / Canvas,
            CenterYFraction: cy / Canvas,
            AreaFraction: area,
            BottomYFraction: bbox[2] / Canvas);
    }

    private static HorizontalZone HorizontalOf(double cx)
    {
        var dead = CenterBandFraction * Canvas;
        if (cx < Canvas / 2.0 - dead) return HorizontalZone.Left;
        if (cx > Canvas / 2.0 + dead) return HorizontalZone.Right;
        return HorizontalZone.Center;
    }

    private static VerticalBand BandOf(double cy) => BandOfEdge(cy);

    private static VerticalBand BandOfEdge(double y) =>
        y < BandLow ? VerticalBand.Sky : y < BandHigh ? VerticalBand.Middle : VerticalBand.Ground;

    private static List<RegionRelation> BuildRelations(IReadOnlyList<Element> elements, List<int> placed)
    {
        var relations = new List<RegionRelation>();
        if (placed.Count < 2) return relations;

        // Nearest-center neighbour per placed element — keeps an otherwise-disjoint element connected to the
        // graph. Stored as canonical unordered pairs so they merge with the overlap/support pairs.
        var nearest = new HashSet<(int, int)>();
        foreach (var i in placed)
        {
            var best = -1;
            var bestDist = double.MaxValue;
            var (ciy, cix) = BboxMath.Center(elements[i].Bbox!);
            foreach (var j in placed)
            {
                if (j == i) continue;
                var (cjy, cjx) = BboxMath.Center(elements[j].Bbox!);
                var dist = (ciy - cjy) * (ciy - cjy) + (cix - cjx) * (cix - cjx);
                if (dist < bestDist) { bestDist = dist; best = j; }
            }
            if (best >= 0) nearest.Add((Math.Min(i, best), Math.Max(i, best)));
        }

        for (var a = 0; a < placed.Count; a++)
        {
            for (var b = a + 1; b < placed.Count; b++)
            {
                var i = placed[a];
                var j = placed[b];
                var boxI = elements[i].Bbox!;
                var boxJ = elements[j].Bbox!;

                var iou = BboxMath.IoU(boxI, boxJ);
                var overlaps = iou > 0;

                // Support sets the relation's direction (From rests on To); fall back to lower-index From.
                var supportIj = DetectSupport(boxI, boxJ);
                var supportJi = DetectSupport(boxJ, boxI);
                int from, to;
                SupportRelation support;
                if (supportIj != SupportRelation.None) { from = i; to = j; support = supportIj; }
                else if (supportJi != SupportRelation.None) { from = j; to = i; support = supportJi; }
                else { from = i; to = j; support = SupportRelation.None; }

                if (!overlaps && support == SupportRelation.None && !nearest.Contains((i, j)))
                    continue;

                var fromBox = elements[from].Bbox!;
                var toBox = elements[to].Bbox!;
                relations.Add(new RegionRelation(
                    from, to,
                    Position: PositionOf(fromBox, toBox),
                    Support: support,
                    Overlaps: overlaps,
                    Iou: iou,
                    FromDepthCue: overlaps ? DepthCueOf(fromBox, toBox) : null));
            }
        }

        return relations;
    }

    /// <summary>Position of <paramref name="fromBox"/> relative to <paramref name="toBox"/> by center offset.</summary>
    private static RelativePosition PositionOf(int[] fromBox, int[] toBox)
    {
        var (fy, fx) = BboxMath.Center(fromBox);
        var (ty, tx) = BboxMath.Center(toBox);
        var dx = fx - tx;
        var dy = fy - ty;
        var dead = CenterBandFraction * Canvas;
        if (Math.Abs(dx) < dead && Math.Abs(dy) < dead) return RelativePosition.Aligned;
        if (Math.Abs(dx) >= Math.Abs(dy)) return dx < 0 ? RelativePosition.LeftOf : RelativePosition.RightOf;
        return dy < 0 ? RelativePosition.Above : RelativePosition.Below;
    }

    /// <summary>
    /// SOFT depth cue for an overlapping pair: <c>From</c> leans nearer when it is BOTH meaningfully larger
    /// AND sits lower in frame than <c>To</c>. Never an index tiebreak — equal/contradictory signals →
    /// <see cref="DepthCue.Ambiguous"/>.
    /// </summary>
    private static DepthCue DepthCueOf(int[] fromBox, int[] toBox)
    {
        var fromArea = (double)BboxMath.Width(fromBox) * BboxMath.Height(fromBox);
        var toArea = (double)BboxMath.Width(toBox) * BboxMath.Height(toBox);
        var fromBottom = fromBox[2] / Canvas;
        var toBottom = toBox[2] / Canvas;

        if (fromArea >= toArea * NearerAreaRatio && fromBottom >= toBottom + NearerBottomDelta)
            return DepthCue.LikelyNearer;
        if (toArea >= fromArea * NearerAreaRatio && toBottom >= fromBottom + NearerBottomDelta)
            return DepthCue.LikelyFarther;
        return DepthCue.Ambiguous;
    }

    /// <summary>Detect how <paramref name="fromBox"/> is supported against <paramref name="toBox"/>.</summary>
    private static SupportRelation DetectSupport(int[] fromBox, int[] toBox)
    {
        var tol = SupportBandFraction * Canvas;

        // RestingOn: From's bottom edge meets To's top edge, From above, with horizontal overlap.
        if (Math.Abs(fromBox[2] - toBox[0]) <= tol
            && fromBox[2] <= toBox[2]
            && SpanOverlapRatio(fromBox[1], fromBox[3], toBox[1], toBox[3]) >= SupportOverlapMin)
            return SupportRelation.RestingOn;

        // Mounted: From sits inside To's face (a sign on a wall) — contained with real overlap.
        if (BboxMath.IoU(fromBox, toBox) > 0 && IsContained(fromBox, toBox, tol))
            return SupportRelation.Mounted;

        // LeaningAgainst: From's vertical edge touches To's, with vertical overlap.
        var touchesRight = Math.Abs(fromBox[3] - toBox[1]) <= tol;
        var touchesLeft = Math.Abs(fromBox[1] - toBox[3]) <= tol;
        if ((touchesRight || touchesLeft)
            && SpanOverlapRatio(fromBox[0], fromBox[2], toBox[0], toBox[2]) >= SupportOverlapMin)
            return SupportRelation.LeaningAgainst;

        return SupportRelation.None;
    }

    /// <summary>Overlap of two 1-D spans as a fraction of the SMALLER span (0 when disjoint).</summary>
    private static double SpanOverlapRatio(int aMin, int aMax, int bMin, int bMax)
    {
        var overlap = Math.Min(aMax, bMax) - Math.Max(aMin, bMin);
        if (overlap <= 0) return 0;
        var smaller = Math.Min(aMax - aMin, bMax - bMin);
        return smaller <= 0 ? 0 : (double)overlap / smaller;
    }

    /// <summary>True when <paramref name="inner"/> sits within <paramref name="outer"/> (with tolerance) and is the smaller box.</summary>
    private static bool IsContained(int[] inner, int[] outer, double tol)
    {
        var innerArea = (double)BboxMath.Width(inner) * BboxMath.Height(inner);
        var outerArea = (double)BboxMath.Width(outer) * BboxMath.Height(outer);
        return innerArea < outerArea
            && inner[0] >= outer[0] - tol && inner[1] >= outer[1] - tol
            && inner[2] <= outer[2] + tol && inner[3] <= outer[3] + tol;
    }
}
