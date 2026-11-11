using System.Text.Json;

namespace Radiacode.App;

/// <summary>User settings, stored as JSON in the per-user local app data folder (Windows and Linux).</summary>
public sealed class AppSettings
{
    /// <summary>0 = follow system, 1 = light, 2 = dark.</summary>
    public int Theme { get; set; }
    public bool Animations { get; set; } = true;
    /// <summary>0 = automatic (USB, then Bluetooth), 1 = USB only, 2 = Bluetooth only.</summary>
    public int Connection { get; set; }
    /// <summary>Serial of the last connected device; preferred when several are found.</summary>
    public string? LastDevice { get; set; }
    /// <summary>Last battery/dose report, used until the device sends a new one after reconnecting.</summary>
    public DeviceStatusCache? LastStatus { get; set; }
    /// <summary>Correct the energy scale using the K-40 peak.</summary>
    public bool GainCorrection { get; set; } = true;
    public bool AlarmEnabled { get; set; } = true;
    /// <summary>Dose-rate alarm level, nSv/h.</summary>
    public double AlarmLevel { get; set; } = 300;
    public bool AlarmNotifications { get; set; } = true;
    /// <summary>Count rate that gives the logo its "resting" pulse (typical indoor background).</summary>
    public double BackgroundCps { get; set; } = 9.0;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadiacodeCompanion", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Settings are a convenience; never crash the app over them.
        }
    }
}

public sealed record DeviceStatusCache(string Serial, DateTime Time, double BatteryPercent, double DoseMicroSv, double DurationSeconds);
