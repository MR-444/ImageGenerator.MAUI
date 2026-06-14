using System.Globalization;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Small color helpers for palette operators. Hex is the schema's only color form (uppercase
/// <c>#RRGGBB</c>); this converts to HSL components so operators can reason about saturation and hue.
/// </summary>
public static class ColorMath
{
    /// <summary>
    /// Parses an uppercase <c>#RRGGBB</c> string to 0–1 RGB. Returns false for anything malformed.
    /// </summary>
    public static bool TryParseHex(string? hex, out double r, out double g, out double b)
    {
        r = g = b = 0;
        if (hex is null || hex.Length != 7 || hex[0] != '#')
            return false;

        if (!byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rb) ||
            !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var gb) ||
            !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bb))
            return false;

        r = rb / 255.0;
        g = gb / 255.0;
        b = bb / 255.0;
        return true;
    }

    /// <summary>
    /// HSL saturation (0–1) of a hex color; 0 for grays or unparseable input. Used to surface "accent"
    /// colors (the most chromatic) when trimming/ordering a palette.
    /// </summary>
    public static double Saturation(string? hex)
    {
        if (!TryParseHex(hex, out var r, out var g, out var b))
            return 0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta <= 0)
            return 0;

        var lightness = (max + min) / 2.0;
        return lightness <= 0.5 ? delta / (max + min) : delta / (2.0 - max - min);
    }

    /// <summary>RGB (each 0–1) → HSL with H in [0,360), S and L in [0,1].</summary>
    public static (double H, double S, double L) RgbToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var l = (max + min) / 2.0;

        if (delta <= 0)
            return (0, 0, l);

        var s = l <= 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

        double h;
        if (max == r)
            h = (g - b) / delta + (g < b ? 6.0 : 0.0);
        else if (max == g)
            h = (b - r) / delta + 2.0;
        else
            h = (r - g) / delta + 4.0;

        return (h * 60.0, s, l);
    }

    /// <summary>HSL (H wrapped mod 360; S, L clamped 0–1) → RGB each 0–1.</summary>
    public static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        h = ((h % 360.0) + 360.0) % 360.0;
        s = Math.Clamp(s, 0.0, 1.0);
        l = Math.Clamp(l, 0.0, 1.0);

        if (s <= 0)
            return (l, l, l);

        var q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        var p = 2.0 * l - q;
        var hk = h / 360.0;

        return (
            HueToChannel(p, q, hk + 1.0 / 3.0),
            HueToChannel(p, q, hk),
            HueToChannel(p, q, hk - 1.0 / 3.0));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        t = ((t % 1.0) + 1.0) % 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }

    /// <summary>
    /// 0–1 RGB → uppercase <c>#RRGGBB</c> (rounds, clamps each channel to 0–255). Guaranteed to match the
    /// validator's <c>^#[0-9A-F]{6}$</c> — uppercase hex via <c>X2</c> + <see cref="string.ToUpperInvariant"/>.
    /// </summary>
    public static string FormatHex(double r, double g, double b)
    {
        var rb = Math.Clamp((int)Math.Round(r * 255.0), 0, 255);
        var gb = Math.Clamp((int)Math.Round(g * 255.0), 0, 255);
        var bb = Math.Clamp((int)Math.Round(b * 255.0), 0, 255);
        return $"#{rb:X2}{gb:X2}{bb:X2}".ToUpperInvariant();
    }

    /// <summary>
    /// Parses <paramref name="hex"/>, applies an HSL <paramref name="transform"/>, and reformats to uppercase
    /// <c>#RRGGBB</c>. Returns <c>null</c> when the input hex is unparseable.
    /// </summary>
    public static string? TransformHsl(
        string? hex,
        Func<(double H, double S, double L), (double H, double S, double L)> transform)
    {
        if (!TryParseHex(hex, out var r, out var g, out var b))
            return null;

        var (h, s, l) = transform(RgbToHsl(r, g, b));
        var (nr, ng, nb) = HslToRgb(h, s, l);
        return FormatHex(nr, ng, nb);
    }
}
