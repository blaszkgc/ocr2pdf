namespace OCR2PDF;

public static class SelectionGeometry
{
    public static Rect Move(Rect rect, Vector delta, Size bounds) => new(
        Math.Clamp(rect.X + delta.X, 0, Math.Max(0, bounds.Width - rect.Width)),
        Math.Clamp(rect.Y + delta.Y, 0, Math.Max(0, bounds.Height - rect.Height)), rect.Width, rect.Height);

    public static Int32Rect ToPixels(Rect rect, Size surface, int pixelWidth, int pixelHeight)
    {
        double sx = pixelWidth / surface.Width, sy = pixelHeight / surface.Height;
        int left = Math.Clamp((int)Math.Round(rect.Left * sx), 0, pixelWidth);
        int top = Math.Clamp((int)Math.Round(rect.Top * sy), 0, pixelHeight);
        int right = Math.Clamp((int)Math.Round(rect.Right * sx), left, pixelWidth);
        int bottom = Math.Clamp((int)Math.Round(rect.Bottom * sy), top, pixelHeight);
        return new(left, top, right - left, bottom - top);
    }
}
