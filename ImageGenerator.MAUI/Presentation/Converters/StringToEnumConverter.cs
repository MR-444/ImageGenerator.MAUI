using System.Globalization;

namespace ImageGenerator.MAUI.Presentation.Converters;

/// <summary>
/// A value converter that facilitates bidirectional conversion between an Enum value and its string representation.
/// </summary>
public class StringToEnumConverter : IValueConverter
{
    /// Converts the provided Enum value to a lowercase string representation.
    /// <param name="value">The value to be converted. It is expected to be an Enum type.</param>
    /// <param name="targetType">The target type of the conversion. This parameter is not used in the method.</param>
    /// <param name="parameter">An optional parameter for the conversion. This parameter is not used in the method.</param>
    /// <param name="culture">The culture information to use during conversion. This parameter is not used in the method.</param>
    /// <returns>If the input value is of type Enum, it returns the Enum value as a lowercase string. Otherwise, it returns the input value as is or null if the input is null.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return enumValue.ToString().ToLower();
        }
        return value;
    }

    /// Converts a string value back to the corresponding enum value.
    /// <param name="value">The value to be converted back, expected to be a string representation of the enum.</param>
    /// <param name="targetType">The target type, which must be an enum type.</param>
    /// <param name="parameter">An optional parameter not used in this implementation.</param>
    /// <param name="culture">The culture-specific information not used in this implementation.</param>
    /// <return>Returns the corresponding enum value if the conversion is successful; otherwise, returns the input value.</return>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringValue && targetType.IsEnum)
        {
            return Enum.Parse(targetType, stringValue, true);
        }
        return value;
    }
} 