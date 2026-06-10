using FluentAssertions;
using ImageGenerator.MAUI.Presentation.Common;

namespace ImageGenerator.MAUI.Tests.Presentation.Common;

public class PaletteSwatchesTests
{
    [Fact]
    public void From_ParsesValidEntries_AndSkipsGarbage()
    {
        var swatches = PaletteSwatches.From("#B5322B, not-a-color, ff0000, #GGGGGG");

        // "ff0000" is normalized to "#FF0000" by ParsePalette; the two garbage entries vanish.
        swatches.Select(s => s.Hex).Should().Equal("#B5322B", "#FF0000");
        swatches[0].Color.ToArgbHex().Should().Be("#B5322B");
    }

    [Fact]
    public void From_Blank_ReturnsEmpty()
    {
        PaletteSwatches.From("   ").Should().BeEmpty();
    }

    [Theory]
    [InlineData(181, 50, 43, "#B5322B")]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(255, 255, 255, "#FFFFFF")]
    [InlineData(-10, 300, 128, "#00FF80")] // out-of-range channels clamp
    public void ToHex_FormatsUppercaseRrggbb(int r, int g, int b, string expected)
    {
        PaletteSwatches.ToHex(r, g, b).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "#112233", "#112233")]
    [InlineData("#AABBCC", "#112233", "#AABBCC, #112233")]
    [InlineData("#AABBCC, ", "#112233", "#AABBCC, #112233")] // trailing comma/space tolerated
    public void Append_BuildsCommaSeparatedText(string existing, string hex, string expected)
    {
        PaletteSwatches.Append(existing, hex).Should().Be(expected);
    }

    [Fact]
    public void RemoveFirst_RemovesOnlyTheFirstOccurrence_AndRebuildsText()
    {
        PaletteSwatches.RemoveFirst("#AABBCC, #112233, #AABBCC", "#AABBCC")
            .Should().Be("#112233, #AABBCC");
    }

    [Fact]
    public void RemoveFirst_UnknownHex_LeavesEntriesIntact()
    {
        PaletteSwatches.RemoveFirst("#AABBCC, #112233", "#FFFFFF")
            .Should().Be("#AABBCC, #112233");
    }
}
