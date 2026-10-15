using System.Diagnostics;
namespace OCR2PDF;

public sealed record OcrLanguage(string Code, string DisplayName);
public static class TesseractDiscovery
{
    public static async Task<IReadOnlyList<OcrLanguage>> GetLanguagesAsync(string executable, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("--list-langs");
        using var process = new Process { StartInfo = info };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(stdout, stderr);
            token.ThrowIfCancellationRequested(); throw new InvalidOperationException("Tesseract did not respond. Check the executable path.");
        }
        string output = await stdout, errors = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(errors.Trim());
        // Some builds write the language list to stderr. Ignore orientation-only data.
        return (output + "\n" + errors).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^[\w/]+$") && line != "osd")
            .Distinct().Select(code => new OcrLanguage(code, DisplayName(code))).OrderBy(language => language.DisplayName).ToArray();
    }
    private static string DisplayName(string code) => code switch
    {
        "eng" => "English", "spa" => "Spanish", "fra" => "French", "deu" => "German", "pol" => "Polish",
        "ita" => "Italian", "por" => "Portuguese", "jpn" => "Japanese", "kor" => "Korean", "chi_sim" => "Chinese (Simplified)",
        "chi_tra" => "Chinese (Traditional)", "ara" => "Arabic", "rus" => "Russian", "ukr" => "Ukrainian", "nld" => "Dutch", _ => code
    };
}
