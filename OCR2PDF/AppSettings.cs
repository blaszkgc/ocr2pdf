using System.Text.Json;
namespace OCR2PDF;
public sealed record AppSettings(string TesseractPath, string Language, bool A4, bool Landscape, bool WriteText)
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OCR2PDF", "settings.json");
    public static AppSettings Load()
    {
        try { if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } settings) return settings; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe");
        return new(File.Exists(installed) ? installed : "tesseract", "eng", false, false, true);
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
