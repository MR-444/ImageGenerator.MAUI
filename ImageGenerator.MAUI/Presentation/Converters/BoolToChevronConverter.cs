using System.Globalization;

namespace ImageGenerator.MAUI.Presentation.Converters;

/// <summary>
/// Maps a section's expanded/collapsed state (bool) to a disclosure chevron glyph:
/// expanded → "▾", collapsed → "▸". Used by the accordion headers on the structure editor so
/// the chevron follows the body's IsVisible without any ViewModel state.
/// </summary>
public sealed class BoolToChevronConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool and true ? "▾" : "▸";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
