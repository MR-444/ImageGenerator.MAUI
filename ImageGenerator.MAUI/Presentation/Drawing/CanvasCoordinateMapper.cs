using ImageGenerator.MAUI.Core.Domain.Ideogram;

namespace ImageGenerator.MAUI.Presentation.Drawing;

/// <summary>A corner of a bbox, for the resize-handle hit-test and drag state.</summary>
public enum BboxCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

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

    /// <summary>
    /// Parses the leading "W:H" of a ComfyUI ResolutionSelector combo label
    /// (e.g. "3:4 (Portrait Standard)" → 3, 4). False for blanks or anything malformed —
    /// callers fall back to a square canvas then.
    /// </summary>
    public static bool TryParseAspectRatioLabel(string? label, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(label)) return false;

        var space = label.IndexOf(' ');
        var ratio = space > 0 ? label[..space] : label;

        var colon = ratio.IndexOf(':');
        if (colon <= 0 || colon >= ratio.Length - 1) return false;

        return int.TryParse(ratio[..colon], out width)
               && int.TryParse(ratio[(colon + 1)..], out height)
               && width > 0 && height > 0;
    }

    /// <summary>True when the grid point lies inside (or on the edge of) the bbox.</summary>
    public static bool BboxContains(int[] bbox, int gridX, int gridY) =>
        bbox.Length == 4 && gridY >= bbox[0] && gridY <= bbox[2] && gridX >= bbox[1] && gridX <= bbox[3];

    /// <summary>
    /// Which corner handle (if any) of the bbox lies within <paramref name="radius"/> pixels
    /// of the pointer. Works in PIXEL space deliberately: a grid-unit radius would stretch
    /// with the canvas aspect ratio, making handles harder to grab on one axis. The nearest
    /// in-radius corner wins (matters for boxes smaller than 2×radius).
    /// </summary>
    public static BboxCorner? HitCorner(
        int[] bbox, float pixelX, float pixelY, float canvasWidth, float canvasHeight, float radius)
    {
        if (bbox.Length != 4) return null;
        var rect = BboxToRect(bbox, canvasWidth, canvasHeight);

        (BboxCorner Corner, float X, float Y)[] corners =
        [
            (BboxCorner.TopLeft, rect.Left, rect.Top),
            (BboxCorner.TopRight, rect.Right, rect.Top),
            (BboxCorner.BottomLeft, rect.Left, rect.Bottom),
            (BboxCorner.BottomRight, rect.Right, rect.Bottom)
        ];

        BboxCorner? best = null;
        var bestDistSq = radius * radius;
        foreach (var (corner, x, y) in corners)
        {
            var dx = pixelX - x;
            var dy = pixelY - y;
            var distSq = dx * dx + dy * dy;
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = corner;
            }
        }
        return best;
    }
}
