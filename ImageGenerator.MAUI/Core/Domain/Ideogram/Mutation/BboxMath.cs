namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Geometry helpers for the SCENE bbox operators. Bboxes are <c>[y_min, x_min, y_max, x_max]</c> on the
/// fixed 0–1000 grid (<see cref="V4JsonPrompt.CanvasSize"/>); callers repair results back onto the grid via
/// <see cref="V4JsonPromptValidator.ClampBbox"/>. All randomness is drawn from an injected
/// <see cref="Random"/> so operators stay deterministic per seed.
/// </summary>
public static class BboxMath
{
    /// <summary>Gaussian sigma (grid units) for each strength: 2 / 5 / 10% of the 0–1000 canvas.</summary>
    public static double SigmaFor(MutationStrength strength) => strength switch
    {
        MutationStrength.Subtle => 0.02 * V4JsonPrompt.CanvasSize,
        MutationStrength.Bold => 0.10 * V4JsonPrompt.CanvasSize,
        _ => 0.05 * V4JsonPrompt.CanvasSize
    };

    /// <summary>
    /// One standard-normal sample via Box–Muller. Draws exactly two <see cref="Random.NextDouble"/> values
    /// and returns only the first variate — there is deliberately NO static cache of the second variate, as
    /// that would couple successive calls and break per-seed reproducibility. The first draw is floored to a
    /// tiny epsilon so <see cref="Math.Log(double)"/> never hits zero.
    /// </summary>
    public static double NextGaussian(Random rng)
    {
        var u1 = Math.Max(rng.NextDouble(), 1e-12);
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>N(0, sigma) sample = sigma × <see cref="NextGaussian(Random)"/>.</summary>
    public static double NextGaussian(Random rng, double sigma) => sigma * NextGaussian(rng);

    /// <summary>Translate by integer dy, dx, preserving width/height. Caller clamps.</summary>
    public static int[] Translate(int[] bbox, int dy, int dx) =>
        [bbox[0] + dy, bbox[1] + dx, bbox[2] + dy, bbox[3] + dx];

    /// <summary>
    /// Scale about the box's own center by independent factors (sy, sx), rounding to int. Preserves the
    /// center, changes the size. Caller clamps + checks degeneracy.
    /// </summary>
    public static int[] Scale(int[] bbox, double sy, double sx)
    {
        var (cy, cx) = Center(bbox);
        var halfH = Height(bbox) / 2.0 * sy;
        var halfW = Width(bbox) / 2.0 * sx;
        return
        [
            (int)Math.Round(cy - halfH),
            (int)Math.Round(cx - halfW),
            (int)Math.Round(cy + halfH),
            (int)Math.Round(cx + halfW)
        ];
    }

    /// <summary>Height (y_max − y_min) on the grid.</summary>
    public static int Height(int[] bbox) => bbox[2] - bbox[0];

    /// <summary>Width (x_max − x_min) on the grid.</summary>
    public static int Width(int[] bbox) => bbox[3] - bbox[1];

    /// <summary>Center as fractional (cy, cx).</summary>
    public static (double cy, double cx) Center(int[] bbox) =>
        ((bbox[0] + bbox[2]) / 2.0, (bbox[1] + bbox[3]) / 2.0);

    /// <summary>True when either span is below <paramref name="minSpan"/> (collapsed / near-degenerate box).</summary>
    public static bool IsDegenerate(int[] bbox, int minSpan) =>
        Width(bbox) < minSpan || Height(bbox) < minSpan;

    /// <summary>Intersection-over-union of two boxes (0–1); 0 when disjoint or either is empty.</summary>
    public static double IoU(int[] a, int[] b)
    {
        var iy1 = Math.Max(a[0], b[0]);
        var ix1 = Math.Max(a[1], b[1]);
        var iy2 = Math.Min(a[2], b[2]);
        var ix2 = Math.Min(a[3], b[3]);

        var ih = iy2 - iy1;
        var iw = ix2 - ix1;
        if (ih <= 0 || iw <= 0)
            return 0;

        var inter = (double)ih * iw;
        var areaA = (double)Height(a) * Width(a);
        var areaB = (double)Height(b) * Width(b);
        var union = areaA + areaB - inter;
        return union <= 0 ? 0 : inter / union;
    }

    /// <summary>
    /// Grid aspect (x-span / y-span) a visually-square object must have on a
    /// <paramref name="targetWidth"/>×<paramref name="targetHeight"/> frame. The 0–1000 grid is square but
    /// the frame is not, so a round object needs grid aspect H/W; on wide frames (W&gt;H) this is &lt;1 — a
    /// narrower x-span, which also gives a single subject a narrow x-span on a wide frame.
    /// </summary>
    public static double GridAspectForSquareObject(int targetWidth, int targetHeight) =>
        targetWidth <= 0 ? 1.0 : (double)targetHeight / targetWidth;
}
