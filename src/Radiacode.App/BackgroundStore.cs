using System.Text.Json;
using Radiacode.Core;

namespace Radiacode.App;

/// <summary>A saved reference ("background") spectrum, kept next to the settings.</summary>
public sealed class BackgroundSpectrum
{
    public DateTime Saved { get; set; }
    public string Device { get; set; } = "";
    public double DurationSeconds { get; set; }
    public float A0 { get; set; }
    public float A1 { get; set; }
    public float A2 { get; set; }
    public int[] Counts { get; set; } = [];

    public long TotalCounts => Counts.Sum(c => (long)c);

    private static string FilePath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "background.json");

    public static BackgroundSpectrum From(Spectrum s, string device) => new()
    {
        Saved = DateTime.Now, Device = device, DurationSeconds = s.Duration.TotalSeconds,
        A0 = s.A0, A1 = s.A1, A2 = s.A2, Counts = (int[])s.Counts.Clone(),
    };

    public static BackgroundSpectrum? Load()
    {
        try { return JsonSerializer.Deserialize<BackgroundSpectrum>(File.ReadAllText(FilePath)); }
        catch { return null; }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }

    public static void Delete()
    {
        try { File.Delete(FilePath); } catch { /* nothing to delete */ }
    }
}
