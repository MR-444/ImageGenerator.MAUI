using System.Text.RegularExpressions;
using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Common;

/// <summary>One renderable palette chip: the schema hex string plus its MAUI color.</summary>
public sealed record PaletteSwatch(string Hex, Color Color);

/// <summary>
/// Pure helpers between the editor's comma-separated palette text and renderable swatches.
/// Unparseable entries are simply skipped here — they stay visible in the text Entry and the
/// validator names them on Apply, so nothing is silently lost.
/// </summary>
public static partial class PaletteSwatches
{
    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexColorRegex();

    public static IReadOnlyList<PaletteSwatch> From(string paletteText)
    {
        var entries = ElementItemViewModel.ParsePalette(paletteText);
        if (entries is null) return [];

        return entries
            .Where(hex => HexColorRegex().IsMatch(hex))
            .Select(hex => new PaletteSwatch(hex, Color.FromArgb(hex)))
            .ToList();
    }

    /// <summary>Schema-exact formatting: uppercase #RRGGBB.</summary>
    public static string ToHex(int red, int green, int blue) =>
        $"#{Math.Clamp(red, 0, 255):X2}{Math.Clamp(green, 0, 255):X2}{Math.Clamp(blue, 0, 255):X2}";

    /// <summary>True if the normalized palette text already contains <paramref name="hex"/>.</summary>
    public static bool Contains(string paletteText, string hex) =>
        ElementItemViewModel.ParsePalette(paletteText)?.Contains(hex) == true;

    /// <summary>Appends <paramref name="hex"/> to a comma-separated palette text.</summary>
    public static string Append(string paletteText, string hex) =>
        string.IsNullOrWhiteSpace(paletteText) ? hex : $"{paletteText.TrimEnd().TrimEnd(',')}, {hex}";

    /// <summary>Removes the first occurrence of <paramref name="hex"/> and rebuilds the text.</summary>
    public static string RemoveFirst(string paletteText, string hex)
    {
        var entries = ElementItemViewModel.ParsePalette(paletteText)?.ToList();
        if (entries is null) return string.Empty;

        var index = entries.IndexOf(hex);
        if (index >= 0) entries.RemoveAt(index);
        return string.Join(", ", entries);
    }
}
