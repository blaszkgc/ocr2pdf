namespace OCR2PDF;

/// <summary>PDF points; used by both export and the on-screen page preview.</summary>
public sealed record PageLayout(double Width, double Height, double ImageX, double ImageY, double ImageWidth, double ImageHeight)
{
    public const double PointsToDip = 96.0 / 72.0;
    public static PageLayout Calculate(int pixelWidth, int pixelHeight, bool a4, bool landscape)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        double width = a4 ? 595.276 : 612, height = a4 ? 841.89 : 792;
        if (landscape) (width, height) = (height, width);
        double scale = Math.Min(width / pixelWidth, height / pixelHeight);
        double w = pixelWidth * scale, h = pixelHeight * scale;
        return new(width, height, (width - w) / 2, (height - h) / 2, w, h);
    }
}
