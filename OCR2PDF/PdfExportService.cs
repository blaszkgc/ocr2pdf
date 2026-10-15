using System.Diagnostics;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
namespace OCR2PDF;
public sealed record ExportProgress(double Percent, string Message);
public static class PdfExportService
{
    public static async Task ExportAsync(IReadOnlyList<CapturePage> pages, string outputPath, AppSettings settings, IProgress<ExportProgress> progress, CancellationToken token)
    {
        if (pages.Count == 0) throw new ArgumentException("No pages to export.");
        if (string.IsNullOrWhiteSpace(settings.Language)) throw new ArgumentException("Enter an OCR language, such as eng.");
        var temporary = Path.Combine(Path.GetTempPath(), "OCR2PDF", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        string stagedPdf = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!, $".ocr2pdf-{Guid.NewGuid():N}.pdf");
        string stagedText = Path.ChangeExtension(stagedPdf, ".txt");
        try
        {
            using var document = new PdfDocument();
            document.Info.Title = Path.GetFileNameWithoutExtension(outputPath); document.Info.Creator = "OCR2PDF";
            var text = new StringBuilder();
            for (int i = 0; i < pages.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                progress.Report(new(100.0 * i / pages.Count, $"Recognizing page {i + 1} of {pages.Count}…"));
                var prefix = Path.Combine(temporary, $"page-{i}");
                await RunTesseractAsync(settings.TesseractPath, pages[i].ImagePath, prefix, settings.Language, token);
                token.ThrowIfCancellationRequested();
                var page = document.AddPage();
                using var image = XImage.FromFile(pages[i].ImagePath);
                var layout = PageLayout.Calculate(image.PixelWidth, image.PixelHeight, settings.A4, settings.Landscape);
                page.Width = XUnit.FromPoint(layout.Width); page.Height = XUnit.FromPoint(layout.Height);
                using var overlay = XPdfForm.FromFile(prefix + ".pdf");
                using (var graphics = XGraphics.FromPdfPage(page))
                {
                    graphics.DrawImage(image, layout.ImageX, layout.ImageY, layout.ImageWidth, layout.ImageHeight);
                    // The original pixels and invisible text use the same placement and scale.
                    graphics.DrawImage(overlay, layout.ImageX, layout.ImageY, layout.ImageWidth, layout.ImageHeight);
                }
                if (settings.WriteText)
                {
                    if (i > 0) text.Append("\n\f\n");
                    text.Append((await File.ReadAllTextAsync(prefix + ".txt", token)).TrimEnd('\r', '\n', '\f'));
                }
            }
            token.ThrowIfCancellationRequested(); progress.Report(new(98, "Saving outputs…"));
            document.Save(stagedPdf);
            if (settings.WriteText) await File.WriteAllTextAsync(stagedText, text.ToString(), new UTF8Encoding(false), token);
            token.ThrowIfCancellationRequested();
            File.Move(stagedPdf, outputPath, true);
            if (settings.WriteText) File.Move(stagedText, Path.ChangeExtension(outputPath, ".txt"), true);
        }
        finally
        {
            if (File.Exists(stagedPdf)) File.Delete(stagedPdf);
            if (File.Exists(stagedText)) File.Delete(stagedText);
            Directory.Delete(temporary, true);
        }
    }
    private static async Task RunTesseractAsync(string executable, string imagePath, string prefix, string language, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { imagePath, prefix, "-l", language, "--dpi", "96", "--psm", "3", "-c", "textonly_pdf=1", "pdf", "txt" }) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex) { throw new InvalidOperationException("Cannot start Tesseract. Select an installed tesseract.exe in Export Settings.", ex); }
        var errors = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(errors, output); throw;
        }
        await output;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Tesseract failed: {(await errors).Trim()}");
        await errors;
    }
}
