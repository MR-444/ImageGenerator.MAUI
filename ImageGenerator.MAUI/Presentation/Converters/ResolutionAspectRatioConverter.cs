using System.Globalization;

namespace ImageGenerator.MAUI.Presentation.Converters;

/// <summary>
/// Display-only converter for the Ideogram resolution picker: turns a raw "WIDTHxHEIGHT"
/// string (e.g. "2048x2048") into a labelled "RATIO (WIDTHxHEIGHT)" form (e.g. "1:1 (2048x2048)").
/// The aspect ratio is reduced from the dimensions via GCD. Non-pixel values ("Auto", "1K", …),
/// nulls, and anything that doesn't parse are returned unchanged. Used on <c>ItemDisplayBinding</c>
/// only, so the bound/stored resolution value is never altered.
/// </summary>
public sealed class ResolutionAspectRatioConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s)
            return value;

        var x = s.IndexOf('x');
        if (x <= 0 || x >= s.Length - 1)
            return value;

        if (!int.TryParse(s[..x], NumberStyles.None, CultureInfo.InvariantCulture, out var w) ||
            !int.TryParse(s[(x + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
            w <= 0 || h <= 0)
        {
            return value;
        }

        var g = Gcd(w, h);
        return $"{w / g}:{h / g} ({s})";
    }

    // ItemDisplayBinding is display-only (one-way); ConvertBack is never invoked.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;

    private static int Gcd(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }
}
