using System.Globalization;
using FluentAssertions;
using ImageGenerator.MAUI.Presentation.Converters;

namespace ImageGenerator.MAUI.Tests.Converters;

public class ResolutionAspectRatioConverterTests
{
    private readonly ResolutionAspectRatioConverter _converter = new();

    private object? Convert(object? value)
        => _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2048x2048", "1:1 (2048x2048)")]
    [InlineData("2880x1440", "2:1 (2880x1440)")]
    [InlineData("1440x2880", "1:2 (1440x2880)")]
    [InlineData("2560x1440", "16:9 (2560x1440)")]
    [InlineData("1664x2496", "2:3 (1664x2496)")]
    [InlineData("1296x3168", "9:22 (1296x3168)")]
    public void Convert_PixelDimensions_PrependsReducedAspectRatio(string input, string expected)
    {
        Convert(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("1K")]
    [InlineData("abc")]
    [InlineData("1024x")]
    [InlineData("x1024")]
    [InlineData("0x1024")]
    public void Convert_NonPixelOrMalformedString_ReturnsUnchanged(string input)
    {
        Convert(input).Should().Be(input);
    }

    [Fact]
    public void Convert_Null_ReturnsNull()
    {
        Convert(null).Should().BeNull();
    }

    [Fact]
    public void ConvertBack_ReturnsValueUnchanged()
    {
        _converter.ConvertBack("2048x2048", typeof(string), null, CultureInfo.InvariantCulture)
            .Should().Be("2048x2048");
    }
}
