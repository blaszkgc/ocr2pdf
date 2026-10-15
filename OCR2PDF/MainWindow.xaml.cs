using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OCR2PDF;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CapturePage> pages = new();
    private readonly Stack<(CapturePage Page, int Index)> removed = new();
    private readonly string session = Path.Combine(Path.GetTempPath(), "OCR2PDF", Guid.NewGuid().ToString("N"));
    private readonly DispatcherTimer discoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private CancellationTokenSource? operationCancellation, discoveryCancellation;
    private bool initialized, ocrReady, closingRequested, closed, fitPage = true, spacePressed;
    private int previewVersion;
    private string requestedLanguages = "eng";
    private string? lastExport;
    private Point dragStart;
    private CapturePage? draggedPage;
    private Point? panStart;
    private double panHorizontal, panVertical;
    private GridLength expandedSettingsWidth = new(245);

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(session);
        PageList.ItemsSource = pages;
        pages.CollectionChanged += (_, _) => { RenumberPages(); UpdateActions(); };
        var settings = AppSettings.Load();
        TesseractPath.Text = settings.TesseractPath; requestedLanguages = settings.Language;
        PageSize.SelectedIndex = settings.A4 ? 1 : 0;
        Orientation.SelectedIndex = settings.Landscape ? 1 : 0; WriteText.IsChecked = settings.WriteText;
        discoveryTimer.Tick += async (_, _) => { discoveryTimer.Stop(); await RefreshLanguagesAsync(); };
        Deactivated += (_, _) => { spacePressed = false; if (panStart != null) PreviewScroll.ReleaseMouseCapture(); };
        initialized = true; UpdateActions();
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshLanguagesAsync();

    private void UpdateActions()
    {
        if (!initialized || closed) return;
        bool idle = operationCancellation == null;
        int index = PageList.SelectedIndex;
        CaptureActions.IsEnabled = SettingsPanel.IsEnabled = idle;
        UpButton.IsEnabled = idle && index > 0;
        DownButton.IsEnabled = idle && index >= 0 && index < pages.Count - 1;
        RemoveButton.IsEnabled = idle && index >= 0;
        UndoButton.IsEnabled = idle && removed.Count > 0;
        ExportButton.IsEnabled = idle && pages.Count > 0 && ocrReady && Languages.SelectedItems.Count > 0;
        ExportButton.ToolTip = pages.Count == 0 ? "Add at least one page to export." : !ocrReady ? "Configure text recognition in settings." : Languages.SelectedItems.Count == 0 ? "Select a recognition language." : "Export searchable PDF (Ctrl+E)";
    }
    private void RenumberPages()
    {
        for (int i = 0; i < pages.Count; i++) pages[i].PageNumber = i + 1;
        PageCount.Text = $"Pages · {pages.Count}";
        if (PageList.SelectedItem is CapturePage selected) PreviewTitle.Text = $"Page {selected.PageNumber} of {pages.Count}";
    }
    private void AddPage(CapturePage page)
    {
        pages.Add(page); PageList.SelectedItem = page; PageList.ScrollIntoView(page);
    }
    private CapturePage SaveSnapshot(BitmapSource image, string name)
    {
        string path = Path.Combine(session, Guid.NewGuid().ToString("N") + ".png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(path)) encoder.Save(stream);
        return new CapturePage(path, name, image);
    }
    private IReadOnlyList<CapturePage> ReadImages(string path, CancellationToken token)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var result = new List<CapturePage>();
        for (int i = 0; i < decoder.Frames.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            result.Add(SaveSnapshot(decoder.Frames[i], Path.GetFileName(path) + (decoder.Frames.Count > 1 ? $" · {i + 1}" : "")));
        }
        return result;
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (operationCancellation != null) return;
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) == true) await ImportFilesAsync(dialog.FileNames);
    }
    private async Task ImportFilesAsync(string[] paths)
    {
        if (operationCancellation != null) return;
        var token = BeginOperation("Importing images…", true);
        var failures = new List<string>();
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                token.ThrowIfCancellationRequested(); Status.Text = $"Importing {i + 1} of {paths.Length}: {Path.GetFileName(paths[i])}";
                try
                {
                    var imported = await Task.Run(() => ReadImages(paths[i], token), token);
                    foreach (var page in imported) AddPage(page);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(paths[i])}: {ex.Message}"); }
            }
            Status.Text = $"{pages.Count} page(s) ready.";
            if (failures.Count > 0) ShowError("Some images could not be imported:\n\n" + string.Join("\n", failures));
        }
        catch (OperationCanceledException) { Status.Text = $"Import canceled. {pages.Count} page(s) retained."; }
        finally { EndOperation(); }
    }
    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (operationCancellation != null) return;
        try
        {
            var image = Clipboard.GetImage();
            if (image == null) { Status.Text = "Copy an image to the clipboard first."; return; }
            image.Freeze();
            string name = $"Pasted image {pages.Count + 1}";
            var token = BeginOperation("Pasting image…", true);
            try { AddPage(await Task.Run(() => SaveSnapshot(image, name), token)); Status.Text = $"{pages.Count} page(s) ready."; }
            catch (OperationCanceledException) { Status.Text = "Paste canceled."; }
            finally { EndOperation(); }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (operationCancellation != null) return;
        var token = BeginOperation("Select a screen region…", true);
        Hide();
        try
        {
            await Task.Delay(250, token);
            var capture = new CaptureWindow();
            if (capture.ShowDialog() == true && capture.Result is { } image)
                AddPage(await Task.Run(() => SaveSnapshot(image, $"Capture {DateTime.Now:HH:mm:ss}"), token));
            Status.Text = $"{pages.Count} page(s) ready.";
        }
        catch (OperationCanceledException) { Status.Text = "Capture canceled."; }
        catch (Exception ex) { Show(); ShowError(ex.Message); }
        finally { if (!closed) { Show(); Activate(); } EndOperation(); }
    }

    private async void Automate_Click(object sender, RoutedEventArgs e)
    {
        if (operationCancellation != null) return;
        var setup = new AutomationSetupWindow { Owner = this };
        if (setup.ShowDialog() != true || setup.Plan is not { } plan) return;
        var token = BeginOperation("Select the repeated snipping region…", false);
        AutomationStopWindow? monitor = null;
        int captured = 0;
        Hide();
        try
        {
            await Task.Delay(250, token);
            var selection = new CaptureWindow();
            if (selection.ShowDialog() != true) { Status.Text = "Automation setup canceled."; return; }
            var region = selection.ScreenRegion;
            var actionPoint = new System.Drawing.Point(region.X + region.Width / 2, region.Y + region.Height / 2);
            if (plan.Mode != AdvanceMode.KeyPress)
            {
                await Task.Delay(200, token);
                var pointPicker = new CaptureWindow(pickPoint: true);
                if (pointPicker.ShowDialog() != true) { Status.Text = "Automation setup canceled."; return; }
                actionPoint = pointPicker.ScreenPoint;
            }
            var target = DesktopAutomation.WindowAt(actionPoint);
            if (target == IntPtr.Zero) throw new InvalidOperationException("Select a location inside the target application.");
            monitor = new AutomationStopWindow(operationCancellation!); monitor.Show(); monitor.CheckStopKey();
            DesktopAutomation.Focus(target);
            for (int seconds = 3; seconds > 0; seconds--)
            { monitor.SetStatus($"Starting in {seconds}s. Focus the target app. Esc stops."); await Task.Delay(1000, token); }
            await AutomationRunner.RunAsync(plan,
                async (number, cancellation) =>
                {
                    monitor.Hide(); await Task.Delay(150, cancellation);
                    DesktopAutomation.EnsureTarget(target, new System.Drawing.Point(region.X + region.Width / 2, region.Y + region.Height / 2));
                    var page = await Task.Run(() => SaveSnapshot(DesktopAutomation.Capture(region), $"Automated capture {number}"), cancellation);
                    AddPage(page); captured++;
                    Progress.Value = 100.0 * captured / plan.PageCount;
                    monitor.SetStatus($"Captured {captured} of {plan.PageCount}. Esc stops."); monitor.Show();
                },
                async cancellation =>
                {
                    monitor.Hide(); await Task.Delay(150, cancellation);
                    cancellation.ThrowIfCancellationRequested(); DesktopAutomation.Advance(plan, actionPoint, target);
                    monitor.Show();
                }, token);
            Status.Text = $"Automation complete. {captured} capture(s) added; review and export your PDF.";
        }
        catch (OperationCanceledException) { Status.Text = $"Automation stopped. {captured} capture(s) retained."; }
        catch (Exception ex) { Status.Text = $"Automation stopped. {captured} capture(s) retained."; Show(); if (!closingRequested) ShowError(ex.Message); }
        finally { monitor?.Finish(); if (!closed) { Show(); Activate(); } EndOperation(); }
    }

    private async void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        int version = ++previewVersion;
        var page = PageList.SelectedItem as CapturePage;
        Preview.Source = null;
        PageCanvas.Visibility = page == null ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Visibility = page == null ? Visibility.Visible : Visibility.Collapsed;
        PreviewTitle.Text = page == null ? "PDF page preview" : $"Page {page.PageNumber} of {pages.Count}";
        UpdatePageLayout(); UpdateActions();
        if (page == null) return;
        try
        {
            var image = await Task.Run(() => CapturePage.Load(page.ImagePath));
            if (!closed && version == previewVersion) Preview.Source = image;
        }
        catch (Exception ex) { if (!closed && version == previewVersion) ShowError("Could not load page preview: " + ex.Message); }
    }
    private void PageSetting_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) UpdatePageLayout(); }
    private void UpdatePageLayout()
    {
        if (!initialized || PageList.SelectedItem is not CapturePage page) return;
        var layout = PageLayout.Calculate(page.PixelWidth, page.PixelHeight, PageSize.SelectedIndex == 1, Orientation.SelectedIndex == 1);
        double dip = PageLayout.PointsToDip;
        PageCanvas.Width = layout.Width * dip; PageCanvas.Height = layout.Height * dip;
        Preview.Width = layout.ImageWidth * dip; Preview.Height = layout.ImageHeight * dip;
        Canvas.SetLeft(Preview, layout.ImageX * dip); Canvas.SetTop(Preview, layout.ImageY * dip);
        if (fitPage) FitPreview();
    }
    private void FitPreview()
    {
        if (!initialized || PageCanvas.Width <= 0 || double.IsNaN(PageCanvas.Width)) return;
        // SizeChanged runs before the ScrollViewer updates its viewport. Use its
        // new bounds and reserve room for page padding and existing scrollbars.
        double width = PreviewScroll.ActualWidth, height = PreviewScroll.ActualHeight;
        if (width > 60 && height > 60) SetZoom(Math.Min((width - 60) / PageCanvas.Width, (height - 60) / PageCanvas.Height), true);
    }
    private void SetZoom(double scale, bool fit = false)
    {
        fitPage = fit; scale = Math.Clamp(scale, .05, 8);
        PageScale.ScaleX = PageScale.ScaleY = scale;
        ZoomLabel.Content = $"{scale:P0}";
    }
    private void Fit_Click(object sender, RoutedEventArgs e) { fitPage = true; FitPreview(); }
    private void ActualSize_Click(object sender, RoutedEventArgs e) => SetZoom(1);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(PageScale.ScaleX * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(PageScale.ScaleX / 1.25);
    private void PreviewScroll_SizeChanged(object sender, SizeChangedEventArgs e) { if (fitPage) FitPreview(); }
    private void PreviewScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || PageList.SelectedItem == null) return;
        var cursor = e.GetPosition(PreviewScroll);
        double old = PageScale.ScaleX, horizontal = PreviewScroll.HorizontalOffset, vertical = PreviewScroll.VerticalOffset;
        SetZoom(old * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        PreviewScroll.UpdateLayout();
        double ratio = PageScale.ScaleX / old;
        PreviewScroll.ScrollToHorizontalOffset((horizontal + cursor.X) * ratio - cursor.X);
        PreviewScroll.ScrollToVerticalOffset((vertical + cursor.Y) * ratio - cursor.Y); e.Handled = true;
    }
    private void PreviewScroll_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle && !(spacePressed && e.ChangedButton == MouseButton.Left)) return;
        panStart = e.GetPosition(PreviewScroll); panHorizontal = PreviewScroll.HorizontalOffset; panVertical = PreviewScroll.VerticalOffset;
        PreviewScroll.CaptureMouse(); PreviewScroll.Cursor = Cursors.Hand; e.Handled = true;
    }
    private void PreviewScroll_MouseMove(object sender, MouseEventArgs e)
    {
        if (panStart is not { } origin) return;
        var current = e.GetPosition(PreviewScroll);
        PreviewScroll.ScrollToHorizontalOffset(panHorizontal + origin.X - current.X);
        PreviewScroll.ScrollToVerticalOffset(panVertical + origin.Y - current.Y);
    }
    private void PreviewScroll_MouseUp(object sender, MouseButtonEventArgs e) { if (panStart != null) { PreviewScroll.ReleaseMouseCapture(); e.Handled = true; } }
    private void PreviewScroll_LostMouseCapture(object sender, MouseEventArgs e) { panStart = null; PreviewScroll.Cursor = null; }

    private void Up_Click(object sender, RoutedEventArgs e) => MovePage(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => MovePage(1);
    private void MovePage(int delta)
    {
        int index = PageList.SelectedIndex;
        if (operationCancellation != null || index < 0 || index + delta < 0 || index + delta >= pages.Count) return;
        pages.Move(index, index + delta); PageList.SelectedIndex = index + delta; UpdateActions();
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (operationCancellation != null || PageList.SelectedItem is not CapturePage page) return;
        int index = pages.IndexOf(page); removed.Push((page, index)); pages.Remove(page);
        PageList.SelectedIndex = Math.Min(index, pages.Count - 1); UpdateActions();
        Status.Text = $"Removed page {index + 1}. Undo is available.";
    }
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (operationCancellation != null || removed.Count == 0) return;
        var (page, index) = removed.Pop(); pages.Insert(Math.Min(index, pages.Count), page);
        PageList.SelectedItem = page; PageList.ScrollIntoView(page); UpdateActions(); Status.Text = "Page restored.";
    }
    private void PageList_MouseDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(PageList);
        draggedPage = (ItemsControl.ContainerFromElement(PageList, e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext as CapturePage;
    }
    private void PageList_MouseMove(object sender, MouseEventArgs e)
    {
        if (operationCancellation != null || e.LeftButton != MouseButtonState.Pressed || draggedPage == null) return;
        var position = e.GetPosition(PageList);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var page = draggedPage; draggedPage = null; DragDrop.DoDragDrop(PageList, new DataObject(typeof(CapturePage), page), DragDropEffects.Move);
    }
    private void PageList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = operationCancellation == null && e.Data.GetDataPresent(typeof(CapturePage)) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true;
        var position = e.GetPosition(PageList);
        if (FindVisualChild<ScrollViewer>(PageList) is { } scroll)
        {
            if (position.Y < 30) scroll.LineUp(); else if (position.Y > PageList.ActualHeight - 30) scroll.LineDown();
        }
    }
    private void PageList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (operationCancellation != null || e.Data.GetData(typeof(CapturePage)) is not CapturePage page || !pages.Contains(page)) return;
        var target = (ItemsControl.ContainerFromElement(PageList, e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext as CapturePage;
        int destination = target == null ? pages.Count - 1 : pages.IndexOf(target);
        pages.Move(pages.IndexOf(page), destination); PageList.SelectedItem = page; UpdateActions();
    }
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) return match; if (FindVisualChild<T>(child) is { } found) return found; }
        return null;
    }
    private void Images_DragOver(object sender, DragEventArgs e) { e.Effects = operationCancellation == null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void Images_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (operationCancellation == null && e.Data.GetData(DataFormats.FileDrop) is string[] paths) await ImportFilesAsync(paths);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Tesseract executable|tesseract.exe|Executables|*.exe" };
        if (dialog.ShowDialog(this) == true) TesseractPath.Text = dialog.FileName;
    }
    private void TesseractPath_Changed(object sender, TextChangedEventArgs e)
    {
        if (!initialized) return;
        discoveryCancellation?.Cancel(); ocrReady = false; OcrStatus.Text = "Checking text recognition…"; UpdateActions();
        discoveryTimer.Stop(); discoveryTimer.Start();
    }
    private async void RefreshLanguages_Click(object sender, RoutedEventArgs e) { discoveryTimer.Stop(); await RefreshLanguagesAsync(); }
    private async Task RefreshLanguagesAsync()
    {
        if (closed) return;
        var desired = Languages.SelectedItems.Cast<OcrLanguage>().Select(language => language.Code).ToArray();
        if (desired.Length == 0) desired = requestedLanguages.Split('+', StringSplitOptions.RemoveEmptyEntries);
        discoveryCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(); discoveryCancellation = cancellation;
        ocrReady = false; OcrStatus.Text = "Checking text recognition…"; UpdateActions();
        try
        {
            var installed = await TesseractDiscovery.GetLanguagesAsync(TesseractPath.Text.Trim(), cancellation.Token);
            if (closed || cancellation.IsCancellationRequested) return;
            Languages.ItemsSource = installed;
            foreach (var language in installed.Where(language => desired.Contains(language.Code))) Languages.SelectedItems.Add(language);
            if (Languages.SelectedItems.Count == 0 && installed.Count > 0) Languages.SelectedItems.Add(installed.FirstOrDefault(language => language.Code == "eng") ?? installed[0]);
            ocrReady = installed.Count > 0;
            OcrStatus.Text = ocrReady ? "Ready · recognition stays on this computer." : "No recognition languages installed. Add language data, then Refresh.";
            if (!ocrReady) AdvancedSettings.IsExpanded = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!cancellation.IsCancellationRequested && !closed) { OcrStatus.Text = "Text recognition unavailable. Select an installed Tesseract executable below."; AdvancedSettings.IsExpanded = true; } }
        finally { if (ReferenceEquals(discoveryCancellation, cancellation)) discoveryCancellation = null; cancellation.Dispose(); UpdateActions(); }
    }
    private void Languages_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) UpdateActions(); }
    private AppSettings CurrentSettings() => new(TesseractPath.Text.Trim(), Languages.SelectedItems.Count > 0 ? string.Join("+", Languages.SelectedItems.Cast<OcrLanguage>().Select(language => language.Code)) : requestedLanguages, PageSize.SelectedIndex == 1, Orientation.SelectedIndex == 1, WriteText.IsChecked == true);

    private CancellationToken BeginOperation(string message, bool indeterminate)
    {
        operationCancellation = new CancellationTokenSource(); Status.Text = message;
        Progress.Value = 0; Progress.IsIndeterminate = indeterminate; Progress.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible; CancelButton.IsEnabled = true; UpdateActions(); return operationCancellation.Token;
    }
    private void EndOperation()
    {
        operationCancellation?.Dispose(); operationCancellation = null;
        Progress.IsIndeterminate = false; Progress.Visibility = Visibility.Collapsed; CancelButton.Visibility = Visibility.Collapsed;
        UpdateActions(); if (closingRequested) Close();
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!ExportButton.IsEnabled) return;
        var dialog = new SaveFileDialog { Filter = "Searchable PDF|*.pdf", FileName = "Capture.pdf" };
        if (dialog.ShowDialog(this) != true) return;
        if (WriteText.IsChecked == true && File.Exists(Path.ChangeExtension(dialog.FileName, ".txt")) && MessageBox.Show(this, "The accompanying TXT file already exists. Replace it?", "Replace TXT", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var settings = CurrentSettings(); var snapshot = pages.ToArray();
        var token = BeginOperation("Starting recognition…", false);
        try
        {
            settings.Save();
            var reporter = new Progress<ExportProgress>(p => { if (!closed && operationCancellation != null && !closingRequested) { Progress.Value = p.Percent; Status.Text = p.Message; } });
            await Task.Run(() => PdfExportService.ExportAsync(snapshot, dialog.FileName, settings, reporter, token));
            lastExport = dialog.FileName;
            OpenPdfButton.Visibility = OpenFolderButton.Visibility = Visibility.Visible;
            Status.Text = $"Saved {snapshot.Length} page(s) to {Path.GetFileName(lastExport)}" + (settings.WriteText ? " and TXT." : ".");
        }
        catch (OperationCanceledException) { Status.Text = "Export canceled."; }
        catch (Exception ex) { Status.Text = "Export failed."; if (!closingRequested) ShowError(ex.Message); }
        finally { EndOperation(); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { operationCancellation?.Cancel(); CancelButton.IsEnabled = false; Status.Text = "Canceling…"; }
    private void OpenPdf_Click(object sender, RoutedEventArgs e) => OpenOutput(false);
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenOutput(true);
    private void OpenOutput(bool folder)
    {
        if (lastExport == null) return;
        try { Process.Start(new ProcessStartInfo(folder ? Path.GetDirectoryName(lastExport)! : lastExport) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Could not open the output: " + ex.Message); }
    }
    private void SettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        bool hide = SettingsPane.Visibility == Visibility.Visible;
        if (hide) { expandedSettingsWidth = SettingsColumn.Width; SettingsColumn.MinWidth = 0; SettingsColumn.Width = new(0); SettingsDividerColumn.Width = new(0); }
        else { SettingsColumn.MinWidth = 215; SettingsColumn.Width = expandedSettingsWidth; SettingsDividerColumn.Width = new(8); }
        SettingsPane.Visibility = SettingsDivider.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        SettingsToggle.Content = hide ? "Show settings" : "Hide settings";
    }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool editing = Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        var modifiers = Keyboard.Modifiers;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (editing) return;
        if (key == Key.Space) { spacePressed = true; return; }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.C) Capture_Click(this, e);
        else if (modifiers == ModifierKeys.Control && key == Key.O) Import_Click(this, e);
        else if (modifiers == ModifierKeys.Control && key == Key.V) Paste_Click(this, e);
        else if (modifiers == ModifierKeys.Control && key == Key.E) Export_Click(this, e);
        else if (modifiers == ModifierKeys.Control && key == Key.Z) Undo_Click(this, e);
        else if (modifiers == ModifierKeys.Control && key == Key.D0) Fit_Click(this, e);
        else if (modifiers == ModifierKeys.Control && key == Key.D1) ActualSize_Click(this, e);
        else if (modifiers == ModifierKeys.Alt && key == Key.Up) MovePage(-1);
        else if (modifiers == ModifierKeys.Alt && key == Key.Down) MovePage(1);
        else if (modifiers == ModifierKeys.None && key == Key.Delete && PageList.IsKeyboardFocusWithin) Remove_Click(this, e);
        else return;
        e.Handled = true;
    }
    private void Window_PreviewKeyUp(object sender, KeyEventArgs e) { if (e.Key == Key.Space) spacePressed = false; }
    private void ShowError(string message) => MessageBox.Show(this, message, "OCR2PDF", MessageBoxButton.OK, MessageBoxImage.Error);
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (operationCancellation != null) { closingRequested = true; operationCancellation.Cancel(); CancelButton.IsEnabled = false; e.Cancel = true; Status.Text = "Finishing cancellation…"; return; }
        closed = true; previewVersion++; discoveryTimer.Stop(); discoveryCancellation?.Cancel();
        try { CurrentSettings().Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        try { Directory.Delete(session, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
