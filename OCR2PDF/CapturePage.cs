using System.ComponentModel;
using System.Windows.Media.Imaging;
namespace OCR2PDF;

public sealed class CapturePage : INotifyPropertyChanged
{
    public string ImagePath { get; }
    public string Name { get; }
    public BitmapSource Thumbnail { get; }
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    private int pageNumber;
    public int PageNumber
    {
        get => pageNumber;
        set { pageNumber = value; PropertyChanged?.Invoke(this, new(nameof(PageLabel))); PropertyChanged?.Invoke(this, new(nameof(AccessibleName))); }
    }
    public string PageLabel => $"Page {PageNumber}";
    public string Dimensions => $"{PixelWidth} × {PixelHeight} px";
    public string AccessibleName => $"{PageLabel}, {Name}, {Dimensions}";
    public event PropertyChangedEventHandler? PropertyChanged;
    public CapturePage(string imagePath, string name, BitmapSource source)
    {
        ImagePath = imagePath; Name = name; PixelWidth = source.PixelWidth; PixelHeight = source.PixelHeight;
        // Retain a small decode for the list, not the full source for every page.
        Thumbnail = Load(imagePath, 240);
    }
    public static BitmapImage Load(string path, int decodeWidth = 0)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.EndInit(); image.Freeze(); return image;
    }
}
