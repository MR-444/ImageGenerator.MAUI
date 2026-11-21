using System.Globalization;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Presentation.Converters;

/// <summary>
/// Renders a <see cref="ModelProvider"/> as the word a picker should show. Exists because a `Picker`
/// bound straight to an enum renders <c>ToString()</c>: that is how "LocalOllama" reached a screen, on
/// the one page whose other provider picker said "Local". Every provider list goes through this, so the
/// spelling lives in <see cref="ModelProviderLabels"/> and nowhere else.
/// </summary>
public sealed class ModelProviderLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ModelProvider provider
            ? provider.ForList()
            : value?.ToString() ?? string.Empty;

    /// <summary>Labels are display-only; the selected value is the enum, so nothing writes back here.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(ModelProviderLabelConverter)} renders labels only; bind the {nameof(ModelProvider)} value, not its text.");
}
