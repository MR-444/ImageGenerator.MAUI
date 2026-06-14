using System.Text.RegularExpressions;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class ColorMathHslTests
{
    private static readonly Regex HexRegex = new("^#[0-9A-F]{6}$");

    [Theory]
    [InlineData("#FF0000")]   // red
    [InlineData("#00FF00")]   // green
    [InlineData("#0000FF")]   // blue
    [InlineData("#808080")]   // gray
    [InlineData("#C8D6E0")]   // base swatch
    [InlineData("#2C4A3E")]   // base swatch
    [InlineData("#E8B998")]   // base swatch
    public void RgbHsl_RoundTrips(string hex)
    {
        ColorMath.TryParseHex(hex, out var r, out var g, out var b).Should().BeTrue();

        var (h, s, l) = ColorMath.RgbToHsl(r, g, b);
        var (nr, ng, nb) = ColorMath.HslToRgb(h, s, l);

        nr.Should().BeApproximately(r, 1.0 / 255.0);
        ng.Should().BeApproximately(g, 1.0 / 255.0);
        nb.Should().BeApproximately(b, 1.0 / 255.0);
        // And the hex itself survives the trip.
        ColorMath.FormatHex(nr, ng, nb).Should().Be(hex);
    }

    [Fact]
    public void FormatHex_IsUppercase_AndMatchesValidatorRegex()
    {
        // 0xAB = 0.6705..; the X2 path must emit uppercase A/B, not ab.
        var hex = ColorMath.FormatHex(0xAB / 255.0, 0xCD / 255.0, 0xEF / 255.0);
        hex.Should().Be("#ABCDEF");
        HexRegex.IsMatch(hex).Should().BeTrue();
    }

    [Fact]
    public void FormatHex_ClampsOutOfRange()
    {
        ColorMath.FormatHex(1.2, -0.1, 0.5).Should().Be("#FF0080");
    }

    [Fact]
    public void HueRotation_360_IsIdentity()
    {
        var rotated = ColorMath.TransformHsl("#3F6B57", hsl => (hsl.H + 360.0, hsl.S, hsl.L));
        rotated.Should().Be("#3F6B57");
    }

    [Fact]
    public void TransformHsl_UnparseableHex_ReturnsNull()
    {
        ColorMath.TransformHsl("not-a-color", hsl => hsl).Should().BeNull();
        ColorMath.TransformHsl("#GGGGGG", hsl => hsl).Should().BeNull();
    }

    [Fact]
    public void HueRotation_OnGray_IsStillGray()
    {
        // S = 0, so any hue rotation reproduces the same gray.
        ColorMath.TransformHsl("#808080", hsl => (hsl.H + 120.0, hsl.S, hsl.L)).Should().Be("#808080");
    }
}
