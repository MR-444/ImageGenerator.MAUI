namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

/// <summary>
/// SCENE operator: recolors one element's per-element <c>color_palette</c> in HSL — temperature shift, hue
/// (harmony) rotation, saturation scaling, or a single-accent hue swap, one op per variant. The style block's
/// palette is LOOK territory and stays untouched. Palette length never changes, so the ≤5 element cap holds by
/// construction. Returns <c>null</c> when no element carries a palette, or the op changes nothing.
/// </summary>
public sealed class MutatePaletteOperator : ICaptionOperator
{
    private static readonly double[] HarmonyRotations = [30.0, -30.0, 180.0, 120.0];
    private const double WarmHue = 30.0;
    private const double CoolHue = 210.0;
    private const double TemperatureStep = 12.0;
    private const double SaturationStep = 0.2;

    public MutationAxis Axis => MutationAxis.Scene;

    public string Name => "MutatePalette";

    public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context)
    {
        var sourceElements = source.CompositionalDeconstruction.Elements;

        var candidates = new List<int>();
        for (var i = 0; i < sourceElements.Count; i++)
        {
            if (sourceElements[i].ColorPalette is { Count: > 0 })
                candidates.Add(i);
        }

        if (candidates.Count == 0)
            return null;

        var index = candidates[rng.Next(candidates.Count)];
        var original = sourceElements[index].ColorPalette!;

        var recolored = rng.Next(4) switch
        {
            0 => ShiftTemperature(original, rng),
            1 => RotateHarmony(original, rng),
            2 => ShiftSaturation(original, rng),
            _ => SwapAccent(original)
        };

        if (recolored is null || recolored.SequenceEqual(original, StringComparer.Ordinal))
            return null;

        var clone = CaptionClone.Clone(source);
        clone.CompositionalDeconstruction.Elements[index].ColorPalette = recolored;

        return V4JsonPromptValidator.Validate(clone).Count == 0 ? clone : null;
    }

    private static List<string>? ShiftTemperature(IReadOnlyList<string> palette, Random rng)
    {
        var target = rng.Next(2) == 0 ? WarmHue : CoolHue;
        return MapAll(palette, hsl =>
        {
            var delta = ShortestHueDelta(hsl.H, target);
            var step = Math.Sign(delta) * Math.Min(Math.Abs(delta), TemperatureStep);
            return (hsl.H + step, hsl.S, hsl.L);
        });
    }

    private static List<string>? RotateHarmony(IReadOnlyList<string> palette, Random rng)
    {
        var rotation = HarmonyRotations[rng.Next(HarmonyRotations.Length)];
        return MapAll(palette, hsl => (hsl.H + rotation, hsl.S, hsl.L));
    }

    private static List<string>? ShiftSaturation(IReadOnlyList<string> palette, Random rng)
    {
        var factor = rng.Next(2) == 0 ? 1.0 - SaturationStep : 1.0 + SaturationStep;
        return MapAll(palette, hsl => (hsl.H, Math.Clamp(hsl.S * factor, 0.0, 1.0), hsl.L));
    }

    private static List<string>? SwapAccent(IReadOnlyList<string> palette)
    {
        var accentIndex = -1;
        var bestSat = -1.0;
        for (var i = 0; i < palette.Count; i++)
        {
            var sat = ColorMath.Saturation(palette[i]);
            if (sat > bestSat)
            {
                bestSat = sat;
                accentIndex = i;
            }
        }

        if (accentIndex < 0)
            return null;

        var swapped = ColorMath.TransformHsl(palette[accentIndex], hsl => (hsl.H + 180.0, hsl.S, hsl.L));
        if (swapped is null)
            return null;

        var result = palette.ToList();
        result[accentIndex] = swapped;
        return result;
    }

    private static List<string>? MapAll(
        IReadOnlyList<string> palette,
        Func<(double H, double S, double L), (double H, double S, double L)> transform)
    {
        var result = new List<string>(palette.Count);
        foreach (var hex in palette)
        {
            var next = ColorMath.TransformHsl(hex, transform);
            if (next is null)
                return null;            // a malformed swatch aborts the op (rare; the base is schema-clean)
            result.Add(next);
        }

        return result;
    }

    private static double ShortestHueDelta(double from, double to)
    {
        var delta = (to - from) % 360.0;
        if (delta < -180.0) delta += 360.0;
        if (delta > 180.0) delta -= 360.0;
        return delta;
    }
}
