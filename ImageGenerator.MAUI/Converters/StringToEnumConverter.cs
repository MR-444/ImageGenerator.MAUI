using System.Globalization;

namespace ImageGenerator.MAUI.Converters;

public class StringToEnumConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return enumValue.ToString().ToLower();
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringValue && targetType.IsEnum)
        {
            return Enum.Parse(targetType, stringValue, true);
        }
        return value;
    }
} 