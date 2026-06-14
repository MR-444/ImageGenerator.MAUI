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
}
