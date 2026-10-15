using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OCR2PDF;
using PdfSharp.Pdf.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var settings = AppSettings.Load();
        try { Run(args); }
        finally { settings.Save(); }
    }
    private static void Run(string[] args)
    {
        string directory = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/verification");
        Directory.CreateDirectory(directory);
        var pages = new List<CapturePage>();
        foreach (var (width, height, label) in new[] { (1200, 800, "SEARCHABLE ALPHA 12345"), (900, 1200, "SEARCHABLE BETA 67890") })
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(Brushes.CornflowerBlue, null, new Rect(80, 180, 300, 150));
                dc.DrawText(new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 48, Brushes.Black, 1), new Point(80, 80));
                dc.DrawText(new FormattedText("Pictures, colors and layout stay intact.", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 32, Brushes.Black, 1), new Point(80, 400));
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze();
            string path = Path.Combine(directory, $"source-{pages.Count}.png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            pages.Add(new CapturePage(path, label, bitmap));
        }
        var settings = AppSettings.Load() with { Language = "eng", A4 = false, Landscape = false, WriteText = true };
        var progress = new SynchronousProgress();
        string output = Path.Combine(directory, "letter.pdf");
        PdfExportService.ExportAsync(pages, output, settings, progress, CancellationToken.None).GetAwaiter().GetResult();
        using (var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import))
        {
            Require(pdf.PageCount == 2, "page count");
            foreach (var page in pdf.Pages) Require(Math.Abs(page.Width.Point - 612) < .01 && Math.Abs(page.Height.Point - 792) < .01, "consistent Letter dimensions");
        }
        string text = File.ReadAllText(Path.ChangeExtension(output, ".txt"));
        Require(text.Contains("ALPHA") && text.Contains("BETA") && text.IndexOf("ALPHA") < text.IndexOf("BETA"), "TXT content and page order");
        PdfExportService.ExportAsync(pages.AsEnumerable().Reverse().ToArray(), Path.Combine(directory, "a4.pdf"), settings with { A4 = true, Landscape = true, WriteText = false }, progress, CancellationToken.None).GetAwaiter().GetResult();
        using (var pdf = PdfReader.Open(Path.Combine(directory, "a4.pdf"), PdfDocumentOpenMode.Import)) Require(Math.Abs(pdf.Pages[0].Width.Point - 841.89) < .01, "A4 landscape dimensions");
        Require(!File.Exists(Path.Combine(directory, "a4.txt")), "optional TXT");
        File.WriteAllText(Path.Combine(directory, "protected.pdf"), "existing output");
        try
        {
            PdfExportService.ExportAsync(pages, Path.Combine(directory, "protected.pdf"), settings with { Language = "nonexistent_language" }, progress, CancellationToken.None).GetAwaiter().GetResult();
            throw new Exception("Expected invalid language failure");
        }
        catch (InvalidOperationException) { Require(File.ReadAllText(Path.Combine(directory, "protected.pdf")) == "existing output", "failed export preserves destination"); }
        using var cancellation = new CancellationTokenSource();
        var cancelProgress = new CancelProgress(cancellation);
        try
        {
            PdfExportService.ExportAsync(pages, Path.Combine(directory, "canceled.pdf"), settings, cancelProgress, cancellation.Token).GetAwaiter().GetResult();
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { Require(!File.Exists(Path.Combine(directory, "canceled.pdf")), "canceled export leaves no output"); }
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        var list = (System.Windows.Controls.ListBox)window.FindName("PageList");
        Require(!((Button)window.FindName("ExportButton")).IsEnabled, "empty export disabled");
        Pump((Task)Invoke(window, "RefreshLanguagesAsync")!);
        foreach (var page in pages) Invoke(window, "AddPage", page);
        Require(((Button)window.FindName("ExportButton")).IsEnabled, "installed OCR makes populated export ready");
        ((ComboBox)window.FindName("PageSize")).SelectedIndex = 0;
        ((ComboBox)window.FindName("Orientation")).SelectedIndex = 0;
        list.SelectedIndex = 0;
        PumpUntil(() => ((Image)window.FindName("Preview")).Source != null);
        var canvas = (Canvas)window.FindName("PageCanvas");
        var preview = (Image)window.FindName("Preview");
        Require(Math.Abs(canvas.Width - 816) < .01 && Math.Abs(canvas.Height - 1056) < .01, "Letter preview physical size");
        Require(Math.Abs(Canvas.GetTop(preview) - 256) < .01 && Math.Abs(preview.Height - 544) < .01, "preview matches exported placement");
        Require(pages.All(page => page.Thumbnail.PixelWidth <= 240), "small thumbnails");
        Require(((BitmapSource)preview.Source).PixelWidth == 1200, "full-resolution selected preview");
        Invoke(window, "MovePage", 1);
        Require(pages[0].PageNumber == 2 && list.SelectedIndex == 1, "page move and renumbering");
        Click(window, "RemoveButton");
        Require(list.Items.Count == 1 && ((Button)window.FindName("UndoButton")).IsEnabled, "removal permits undo");
        Click(window, "UndoButton");
        Require(list.Items.Count == 2 && list.SelectedItem == pages[0], "undo restores selection and position");
        Invoke(window, "MovePage", -1);
        Require(!((Button)window.FindName("UpButton")).IsEnabled, "first page up disabled");
        Require(SelectionGeometry.ToPixels(new Rect(10, 20, 100, 80), new Size(800, 600), 1600, 1200) == new Int32Rect(20, 40, 200, 160), "DPI-aware crop conversion");
        Require(SelectionGeometry.Move(new Rect(10, 20, 100, 80), new Vector(-99, 999), new Size(800, 600)) == new Rect(0, 520, 100, 80), "selection stays inside display");
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1140, 720)); content.Arrange(new Rect(0, 0, 1140, 720)); content.UpdateLayout();
        PumpUntil(() => preview.Source != null);
        Click(window, "ZoomLabel");
        Require(((ScaleTransform)window.FindName("PageScale")).ScaleX == 1, "100 percent zoom");
        Invoke(window, "Fit_Click", window, new RoutedEventArgs());
        Require(((ScaleTransform)window.FindName("PageScale")).ScaleX < 1, "fit zoom");
        var ui = new RenderTargetBitmap(1140, 720, 96, 96, PixelFormats.Pbgra32);
        ui.Render(content);
        var uiEncoder = new PngBitmapEncoder(); uiEncoder.Frames.Add(BitmapFrame.Create(ui));
        using (var stream = File.Create(Path.Combine(directory, "interface.png"))) uiEncoder.Save(stream);
        content.Measure(new Size(780, 540)); content.Arrange(new Rect(0, 0, 780, 540)); content.UpdateLayout();
        Require(canvas.Width * ((ScaleTransform)window.FindName("PageScale")).ScaleX <= ((ScrollViewer)window.FindName("PreviewScroll")).ActualWidth, "fit at small window size");
        var smallUi = new RenderTargetBitmap(780, 540, 96, 96, PixelFormats.Pbgra32); smallUi.Render(content);
        var smallEncoder = new PngBitmapEncoder(); smallEncoder.Frames.Add(BitmapFrame.Create(smallUi));
        using (var stream = File.Create(Path.Combine(directory, "interface-small.png"))) smallEncoder.Save(stream);
        Click(window, "SettingsToggle");
        Require(((ScrollViewer)window.FindName("SettingsPane")).Visibility == Visibility.Collapsed, "settings collapse");
        Click(window, "SettingsToggle");
        Require(((ScrollViewer)window.FindName("SettingsPane")).Visibility == Visibility.Visible, "settings restore");
        var executableBox = (TextBox)window.FindName("TesseractPath");
        string executable = executableBox.Text;
        executableBox.Text = "missing-tesseract-executable";
        Require(!((Button)window.FindName("ExportButton")).IsEnabled, "changed OCR path invalidates readiness");
        Pump((Task)Invoke(window, "RefreshLanguagesAsync")!);
        Require(!((Button)window.FindName("ExportButton")).IsEnabled && ((Expander)window.FindName("AdvancedSettings")).IsExpanded, "invalid OCR exposes setup");
        executableBox.Text = executable;
        Pump((Task)Invoke(window, "RefreshLanguagesAsync")!);
        Pump((Task)Invoke(window, "ImportFilesAsync", (object)new string[] { Path.Combine(directory, "source-0.png") })!);
        Require(list.Items.Count == 3, "background import");
        PumpUntil(() => ((Image)window.FindName("Preview")).Source != null);
        var operationToken = (CancellationToken)Invoke(window, "BeginOperation", "Test operation", true)!;
        window.Close();
        Require(operationToken.IsCancellationRequested, "close cancels active operation");
        Invoke(window, "EndOperation");
        Require((bool)typeof(MainWindow).GetField("closed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!, "close completes after cancellation");
        var capture = new CaptureWindow();
        var captureContent = (FrameworkElement)capture.Content;
        captureContent.Measure(new Size(960, 540)); captureContent.Arrange(new Rect(0, 0, 960, 540)); captureContent.UpdateLayout();
        typeof(CaptureWindow).GetField("region", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(capture, new Rect(50, 80, 200, 150));
        typeof(CaptureWindow).GetMethod("UpdateSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(capture, null);
        var mask = (System.Windows.Shapes.Path)typeof(CaptureWindow).GetField("dim", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!;
        Require(mask.Data.FillContains(new Point(10, 10)) && !mask.Data.FillContains(new Point(100, 100)), "capture dims only outside selection");
        Require(((Button)typeof(CaptureWindow).GetField("confirm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(capture)!).IsEnabled, "valid capture waits for confirmation");
        capture.Close();
        foreach (var mode in Enum.GetValues<AdvanceMode>())
        {
            var events = new List<string>();
            var plan = new AutomationPlan(mode, 3, .2);
            Pump(AutomationRunner.RunAsync(plan,
                (number, token) => { events.Add($"capture {number}"); return Task.CompletedTask; },
                token => { events.Add("advance"); return Task.CompletedTask; }, CancellationToken.None));
            Require(events.SequenceEqual(new[] { "capture 1", "advance", "capture 2", "advance", "capture 3" }), $"{mode}: capture before advancing and no extra final action");
        }
        using (var stopped = new CancellationTokenSource())
        {
            int captured = 0, advanced = 0;
            try
            {
                Pump(AutomationRunner.RunAsync(new AutomationPlan(AdvanceMode.Scroll, 10, .2),
                    (number, token) => { captured++; return Task.CompletedTask; },
                    token => { advanced++; stopped.Cancel(); return Task.CompletedTask; }, stopped.Token));
                throw new Exception("Expected automation cancellation");
            }
            catch (OperationCanceledException) { Require(captured == 1 && advanced == 1, "stop during wait prevents further captures and actions"); }
        }
        using (var stopToken = new CancellationTokenSource())
        {
            var monitor = new AutomationStopWindow(stopToken);
            new System.Windows.Interop.WindowInteropHelper(monitor).EnsureHandle();
            monitor.CheckStopKey();
            var arguments = new object[] { IntPtr.Zero, 0x0312, new IntPtr(7421), IntPtr.Zero, false };
            typeof(AutomationStopWindow).GetMethod("OnMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(monitor, arguments);
            Require(stopToken.IsCancellationRequested, "global stop message cancels automation");
            monitor.Finish();
            var secondMonitor = new AutomationStopWindow(stopToken);
            new System.Windows.Interop.WindowInteropHelper(secondMonitor).EnsureHandle(); secondMonitor.CheckStopKey(); secondMonitor.Finish();
        }
        try { new AutomationPlan(AdvanceMode.Click, 0, .2).Validate(); throw new Exception("Invalid count accepted"); } catch (ArgumentException) { }
        var setupWindow = new AutomationSetupWindow();
        ((ComboBox)setupWindow.FindName("Mode")).SelectedIndex = 1;
        Require(((StackPanel)setupWindow.FindName("ScrollOptions")).Visibility == Visibility.Visible, "scroll choices available");
        ((ComboBox)setupWindow.FindName("Mode")).SelectedIndex = 2;
        Require(((StackPanel)setupWindow.FindName("KeyOptions")).Visibility == Visibility.Visible, "key choices available");
        setupWindow.Close();
        Console.WriteLine("PASS: automation sequencing in all modes, cancellation, global stop lifecycle, and setup options.");
        Console.WriteLine("PASS: export, image fidelity fixtures, page layout, thumbnails, languages, undo, reordering, settings, import, and close cancellation.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Failed: " + message); }
    private static object? Invoke(MainWindow window, string method, params object[] args) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static void Click(MainWindow window, string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("UI operation timed out");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }
    private sealed class SynchronousProgress : IProgress<ExportProgress> { public void Report(ExportProgress value) => Console.WriteLine(value.Message); }
    private sealed class CancelProgress(CancellationTokenSource source) : IProgress<ExportProgress> { public void Report(ExportProgress value) => source.Cancel(); }
}
