namespace ImageGenerator.MAUI.Presentation.Drawing;

/// <summary>One renderable box: the schema bbox plus what the drawable needs to label it.</summary>
public sealed record CanvasBox(int[] Bbox, string Label, bool IsText, bool IsSelected);

/// <summary>
/// Render-only spatial view of the structured prompt (Phase 1 — no drag/resize; bbox editing
/// happens via the numeric fields, taps select). Pulls boxes lazily through
/// <see cref="BoxesProvider"/> on every draw, so callers only need GraphicsView.Invalidate().
/// Fixed colors instead of theme resources: the canvas mimics a paper artboard, which reads
/// fine in both light and dark app themes.
/// </summary>
public sealed class IdeogramCanvasDrawable : IDrawable
{
    public Func<IReadOnlyList<CanvasBox>>? BoxesProvider { get; set; }

    private static readonly Color CanvasFill = Color.FromArgb("#FAFAFA");
    private static readonly Color GridLine = Color.FromArgb("#E0E0E0");
    private static readonly Color Frame = Color.FromArgb("#9E9E9E");
    private static readonly Color ObjStroke = Color.FromArgb("#1E88E5");
    private static readonly Color ObjFill = Color.FromArgb("#221E88E5");
    private static readonly Color TextStroke = Color.FromArgb("#43A047");
    private static readonly Color TextFill = Color.FromArgb("#2243A047");
    private static readonly Color SelectedStroke = Color.FromArgb("#E53935");
    private static readonly Color LabelColor = Color.FromArgb("#424242");

    private const int GridDivisions = 10;
    private const int MaxLabelLength = 24;
    // Visual size of the resize handles on the selected box. Smaller than the VM's
    // HandleTouchRadius on purpose — the touch target is forgiving, the visual is tidy.
    private const float HandleDrawSize = 8f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // The page shapes the GraphicsView to the target image's aspect ratio (via the
        // VM's Canvas*Request properties), so the full rect IS the 1000x1000 grid surface.
        var width = dirtyRect.Width;
        var height = dirtyRect.Height;
        if (width <= 0 || height <= 0) return;

        canvas.FillColor = CanvasFill;
        canvas.FillRectangle(0, 0, width, height);

        canvas.StrokeColor = GridLine;
        canvas.StrokeSize = 1;
        for (var i = 1; i < GridDivisions; i++)
        {
            var x = width / GridDivisions * i;
            var y = height / GridDivisions * i;
            canvas.DrawLine(x, 0, x, height);
            canvas.DrawLine(0, y, width, y);
        }

        canvas.StrokeColor = Frame;
        canvas.StrokeSize = 2;
        canvas.DrawRectangle(0, 0, width, height);

        var boxes = BoxesProvider?.Invoke();
        if (boxes is null) return;

        canvas.FontSize = 11;
        foreach (var box in boxes)
        {
            if (box.Bbox.Length != 4) continue;
            var rect = CanvasCoordinateMapper.BboxToRect(box.Bbox, width, height);

            canvas.FillColor = box.IsText ? TextFill : ObjFill;
            canvas.FillRectangle(rect);

            canvas.StrokeColor = box.IsSelected ? SelectedStroke : box.IsText ? TextStroke : ObjStroke;
            canvas.StrokeSize = box.IsSelected ? 3 : 1.5f;
            canvas.DrawRectangle(rect);

            if (box.IsSelected) DrawResizeHandles(canvas, rect);

            var label = box.Label.Length > MaxLabelLength ? box.Label[..MaxLabelLength] + "…" : box.Label;
            canvas.FontColor = box.IsSelected ? SelectedStroke : LabelColor;
            canvas.DrawString(label, rect.X + 4, rect.Y + 2, Math.Max(rect.Width - 8, 10), 16,
                HorizontalAlignment.Left, VerticalAlignment.Top);
        }
    }

    /// <summary>Filled squares on the four corners of the selected box — the resize affordance.</summary>
    private static void DrawResizeHandles(ICanvas canvas, RectF rect)
    {
        canvas.FillColor = SelectedStroke;
        const float half = HandleDrawSize / 2;
        Span<PointF> corners =
        [
            new PointF(rect.Left, rect.Top),
            new PointF(rect.Right, rect.Top),
            new PointF(rect.Left, rect.Bottom),
            new PointF(rect.Right, rect.Bottom)
        ];
        foreach (var corner in corners)
        {
            canvas.FillRectangle(corner.X - half, corner.Y - half, HandleDrawSize, HandleDrawSize);
        }
    }
}
