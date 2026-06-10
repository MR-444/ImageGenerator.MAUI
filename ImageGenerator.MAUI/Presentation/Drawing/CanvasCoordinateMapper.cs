using ImageGenerator.MAUI.Core.Domain.Ideogram;

namespace ImageGenerator.MAUI.Presentation.Drawing;

/// <summary>
/// Pure math between the schema's fixed 0–1000 bbox grid and the on-screen pixel square the
/// canvas is rendered into. Kept free of MAUI controls so it is unit-testable; the drawable
/// and the tap hit-test are the only consumers.
/// </summary>
public static class CanvasCoordinateMapper
{
    public static float GridToPixels(int grid, float canvasPixels) =>
        grid / (float)V4JsonPrompt.CanvasSize * canvasPixels;

    /// <summary>Inverse of <see cref="GridToPixels"/>, clamped onto the grid. Returns 0 for a degenerate canvas.</summary>
    public static int PixelsToGrid(float pixels, float canvasPixels)
    {
        if (canvasPixels <= 0) return 0;
        var grid = (int)Math.Round(pixels / canvasPixels * V4JsonPrompt.CanvasSize);
        return Math.Clamp(grid, 0, V4JsonPrompt.CanvasSize);
    }

    /// <summary>
    /// Maps a schema bbox [y_min, x_min, y_max, x_max] to a pixel rectangle. Width and
    /// height scale independently — the canvas mirrors the target image's aspect ratio
    /// while the grid itself stays 0–1000 on both axes.
    /// </summary>
    public static RectF BboxToRect(int[] bbox, float canvasWidth, float canvasHeight)
    {
        var x = GridToPixels(bbox[1], canvasWidth);
        var y = GridToPixels(bbox[0], canvasHeight);
        var width = GridToPixels(bbox[3] - bbox[1], canvasWidth);
        var height = GridToPixels(bbox[2] - bbox[0], canvasHeight);
        return new RectF(x, y, width, height);
    }

    /// <summary>
    /// Parses a "WxH" resolution string (e.g. "1440x2880"). False for the "Auto" sentinel,
    /// blanks, or anything malformed — callers fall back to a square canvas then.
    /// </summary>
    public static bool TryParseResolution(string? resolution, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(resolution)) return false;

        var x = resolution.IndexOf('x');
        if (x <= 0 || x >= resolution.Length - 1) return false;

        return int.TryParse(resolution[..x], out width)
               && int.TryParse(resolution[(x + 1)..], out height)
               && width > 0 && height > 0;
    }

    /// <summary>True when the grid point lies inside (or on the edge of) the bbox.</summary>
    public static bool BboxContains(int[] bbox, int gridX, int gridY) =>
        bbox.Length == 4 && gridY >= bbox[0] && gridY <= bbox[2] && gridX >= bbox[1] && gridX <= bbox[3];
}
