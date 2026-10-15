using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;

namespace OCR2PDF;

public sealed class CaptureWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    private readonly Canvas canvas = new();
    private readonly System.Windows.Shapes.Path dim = new() { Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle outline = new() { Stroke = SystemColors.HighlightBrush, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly List<Rectangle> handles = new();
    private readonly TextBlock dimensions = new() { Foreground = Brushes.White, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button confirm = new() { Content = "Capture", Padding = new Thickness(12, 7, 12, 7), IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Border toolbar;
    private readonly BitmapSource screenshot;
    private Rect region = Rect.Empty, original;
    private Point? dragStart;
    private string mode = "draw";
    private readonly bool pickPoint;
    private readonly Drawing.Rectangle screenBounds;
    public BitmapSource? Result { get; private set; }
    public Drawing.Rectangle ScreenRegion { get; private set; }
    public Drawing.Point ScreenPoint { get; private set; }

    public CaptureWindow(bool pickPoint = false)
    {
        this.pickPoint = pickPoint;
        var bounds = new Drawing.Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        screenBounds = bounds;
        using var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Drawing.Point.Empty, bounds.Size);
        using var stream = new MemoryStream(); bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); screenshot = image;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross; Background = Brushes.Black;
        var grid = new Grid(); grid.Children.Add(new Image { Source = screenshot, Stretch = Stretch.Fill });
        grid.Children.Add(canvas); canvas.Children.Add(dim); canvas.Children.Add(outline);
        for (int i = 0; i < 8; i++)
        {
            var handle = new Rectangle { Width = 8, Height = 8, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            handles.Add(handle); canvas.Children.Add(handle);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(new TextBlock { Text = pickPoint ? "Click the action location · Esc to cancel (this selection does not click the app)" : "Drag to select · adjust edges or move · Enter to capture · Esc to cancel", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        if (pickPoint) confirm.Visibility = Visibility.Collapsed;
        actions.Children.Add(dimensions); actions.Children.Add(confirm);
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 7, 12, 7) }; actions.Children.Add(cancel);
        toolbar = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(5), Padding = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) , Child = actions };
        grid.Children.Add(toolbar); Content = grid;
        confirm.Click += (_, _) => ConfirmSelection(); cancel.Click += (_, _) => DialogResult = false;
        SourceInitialized += (_, _) => PositionWindow(bounds);
        Loaded += (_, _) => { PositionWindow(bounds); Activate(); Focus(); UpdateSelection(); };
        canvas.SizeChanged += (_, _) => UpdateSelection();
        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseDown; MouseMove += OnMouseMove; MouseLeftButtonUp += OnMouseUp;
    }
    private void PositionWindow(Drawing.Rectangle bounds)
    {
        if (!SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040)) throw new System.ComponentModel.Win32Exception();
    }
    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, canvas.ActualWidth), Math.Clamp(point.Y, 0, canvas.ActualHeight));
    private string HitRegion(Point point)
    {
        if (region.IsEmpty) return "draw";
        const double tolerance = 8;
        bool left = Math.Abs(point.X - region.Left) <= tolerance, right = Math.Abs(point.X - region.Right) <= tolerance;
        bool top = Math.Abs(point.Y - region.Top) <= tolerance, bottom = Math.Abs(point.Y - region.Bottom) <= tolerance;
        if (left && top) return "nw"; if (right && top) return "ne"; if (left && bottom) return "sw"; if (right && bottom) return "se";
        if (point.Y >= region.Top && point.Y <= region.Bottom) { if (left) return "w"; if (right) return "e"; }
        if (point.X >= region.Left && point.X <= region.Right) { if (top) return "n"; if (bottom) return "s"; }
        return region.Contains(point) ? "move" : "draw";
    }
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (toolbar.IsMouseOver) return;
        if (pickPoint)
        {
            var point = Clamp(e.GetPosition(canvas));
            ScreenPoint = new Drawing.Point(screenBounds.X + (int)Math.Round(point.X * screenshot.PixelWidth / canvas.ActualWidth), screenBounds.Y + (int)Math.Round(point.Y * screenshot.PixelHeight / canvas.ActualHeight));
            DialogResult = true; e.Handled = true; return;
        }
        dragStart = Clamp(e.GetPosition(canvas)); mode = HitRegion(dragStart.Value); original = region;
        if (mode == "draw") region = new Rect(dragStart.Value, dragStart.Value);
        CaptureMouse(); UpdateSelection(); e.Handled = true;
    }
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var point = Clamp(e.GetPosition(canvas));
        if (dragStart is not { } origin)
        {
            Cursor = HitRegion(point) switch { "move" => Cursors.SizeAll, "nw" or "se" => Cursors.SizeNWSE, "ne" or "sw" => Cursors.SizeNESW, "w" or "e" => Cursors.SizeWE, "n" or "s" => Cursors.SizeNS, _ => Cursors.Cross }; return;
        }
        if (mode == "draw") region = new Rect(origin, point);
        else if (mode == "move") region = SelectionGeometry.Move(original, point - origin, new Size(canvas.ActualWidth, canvas.ActualHeight));
        else
        {
            double left = original.Left, right = original.Right, top = original.Top, bottom = original.Bottom;
            if (mode.Contains('w')) left = Math.Min(point.X, right - 1); if (mode.Contains('e')) right = Math.Max(point.X, left + 1);
            if (mode.Contains('n')) top = Math.Min(point.Y, bottom - 1); if (mode.Contains('s')) bottom = Math.Max(point.Y, top + 1);
            region = new Rect(new Point(left, top), new Point(right, bottom));
        }
        UpdateSelection();
    }
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (dragStart == null) return;
        OnMouseMove(sender, e); dragStart = null; ReleaseMouseCapture(); UpdateSelection(); e.Handled = true;
    }
    private Int32Rect PixelRegion() => SelectionGeometry.ToPixels(region, new Size(canvas.ActualWidth, canvas.ActualHeight), screenshot.PixelWidth, screenshot.PixelHeight);
    private void UpdateSelection()
    {
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight)));
        bool selected = !region.IsEmpty && region.Width > 0 && region.Height > 0;
        if (selected) mask.Children.Add(new RectangleGeometry(region)); dim.Data = mask;
        outline.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        foreach (var handle in handles) handle.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        confirm.IsEnabled = false; dimensions.Text = "";
        if (!selected) return;
        Canvas.SetLeft(outline, region.X); Canvas.SetTop(outline, region.Y); outline.Width = region.Width; outline.Height = region.Height;
        var points = new[] { region.TopLeft, new Point(region.Left + region.Width / 2, region.Top), region.TopRight, new Point(region.Right, region.Top + region.Height / 2), region.BottomRight, new Point(region.Left + region.Width / 2, region.Bottom), region.BottomLeft, new Point(region.Left, region.Top + region.Height / 2) };
        for (int i = 0; i < handles.Count; i++) { Canvas.SetLeft(handles[i], points[i].X - 4); Canvas.SetTop(handles[i], points[i].Y - 4); }
        var pixels = PixelRegion(); dimensions.Text = $"{pixels.Width} × {pixels.Height} px"; confirm.IsEnabled = pixels.Width >= 10 && pixels.Height >= 10;
    }
    private void ConfirmSelection()
    {
        if (!confirm.IsEnabled || dragStart != null) return;
        var pixels = PixelRegion();
        ScreenRegion = new Drawing.Rectangle(screenBounds.X + pixels.X, screenBounds.Y + pixels.Y, pixels.Width, pixels.Height);
        Result = new CroppedBitmap(screenshot, pixels); Result.Freeze(); DialogResult = true;
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        else if (e.Key == Key.Enter) { ConfirmSelection(); e.Handled = true; }
        else if (!region.IsEmpty && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            var delta = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), _ => new Vector(0, step) };
            region = SelectionGeometry.Move(region, delta, new Size(canvas.ActualWidth, canvas.ActualHeight)); UpdateSelection(); e.Handled = true;
        }
    }
}
