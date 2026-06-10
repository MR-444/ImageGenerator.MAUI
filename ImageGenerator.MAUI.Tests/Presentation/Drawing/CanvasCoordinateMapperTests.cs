using FluentAssertions;
using ImageGenerator.MAUI.Presentation.Drawing;

namespace ImageGenerator.MAUI.Tests.Presentation.Drawing;

public class CanvasCoordinateMapperTests
{
    [Theory]
    [InlineData(0, 480, 0)]
    [InlineData(500, 480, 240)]
    [InlineData(1000, 480, 480)]
    [InlineData(250, 1000, 250)]
    public void GridToPixels_ScalesLinearly(int grid, float canvas, float expected)
    {
        CanvasCoordinateMapper.GridToPixels(grid, canvas).Should().BeApproximately(expected, 0.001f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(999)]
    [InlineData(1000)]
    public void PixelsToGrid_RoundTripsGridToPixels(int grid)
    {
        const float canvas = 480f;

        var pixels = CanvasCoordinateMapper.GridToPixels(grid, canvas);

        CanvasCoordinateMapper.PixelsToGrid(pixels, canvas).Should().Be(grid);
    }

    [Theory]
    [InlineData(-10f, 480f, 0)]     // off-canvas left/top
    [InlineData(500f, 480f, 1000)]  // off-canvas right/bottom
    [InlineData(100f, 0f, 0)]       // degenerate canvas mid-layout
    public void PixelsToGrid_ClampsOntoGrid(float pixels, float canvas, int expected)
    {
        CanvasCoordinateMapper.PixelsToGrid(pixels, canvas).Should().Be(expected);
    }

    [Fact]
    public void BboxToRect_MapsSchemaOrderToXywh()
    {
        // bbox is [y_min, x_min, y_max, x_max]; RectF is (x, y, w, h).
        var rect = CanvasCoordinateMapper.BboxToRect([100, 200, 300, 400], 1000f, 1000f);

        rect.X.Should().BeApproximately(200, 0.001f);
        rect.Y.Should().BeApproximately(100, 0.001f);
        rect.Width.Should().BeApproximately(200, 0.001f);
        rect.Height.Should().BeApproximately(200, 0.001f);
    }

    [Fact]
    public void BboxToRect_NonSquareCanvas_ScalesAxesIndependently()
    {
        // Portrait canvas 240x480 (e.g. target 1440x2880): x scales by 0.24, y by 0.48.
        var rect = CanvasCoordinateMapper.BboxToRect([100, 200, 300, 400], 240f, 480f);

        rect.X.Should().BeApproximately(48, 0.001f);    // 200/1000 * 240
        rect.Y.Should().BeApproximately(48, 0.001f);    // 100/1000 * 480
        rect.Width.Should().BeApproximately(48, 0.001f);  // (400-200)/1000 * 240
        rect.Height.Should().BeApproximately(96, 0.001f); // (300-100)/1000 * 480
    }

    [Theory]
    [InlineData("1440x2880", true, 1440, 2880)]
    [InlineData("2048x2048", true, 2048, 2048)]
    [InlineData("Auto", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData(null, false, 0, 0)]
    [InlineData("x100", false, 0, 0)]
    [InlineData("100x", false, 0, 0)]
    [InlineData("0x100", false, 0, 0)]
    [InlineData("axb", false, 0, 0)]
    public void TryParseResolution_ParsesWxH_AndRejectsSentinelsAndGarbage(
        string? input, bool expectedOk, int expectedWidth, int expectedHeight)
    {
        var ok = CanvasCoordinateMapper.TryParseResolution(input, out var width, out var height);

        ok.Should().Be(expectedOk);
        if (expectedOk)
        {
            width.Should().Be(expectedWidth);
            height.Should().Be(expectedHeight);
        }
    }

    [Theory]
    [InlineData(200, 100, true)]   // top-left corner (inclusive)
    [InlineData(400, 300, true)]   // bottom-right corner (inclusive)
    [InlineData(300, 200, true)]   // interior
    [InlineData(199, 200, false)]  // just left
    [InlineData(300, 301, false)]  // just below
    public void BboxContains_TreatsEdgesAsInside(int gridX, int gridY, bool expected)
    {
        CanvasCoordinateMapper.BboxContains([100, 200, 300, 400], gridX, gridY).Should().Be(expected);
    }

    [Fact]
    public void BboxContains_WrongLength_IsNeverAHit()
    {
        CanvasCoordinateMapper.BboxContains([1, 2, 3], 0, 0).Should().BeFalse();
    }
}
