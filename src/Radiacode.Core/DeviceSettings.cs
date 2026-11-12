namespace Radiacode.Core;

/// <summary>Event flags used by the device's sound/vibration control registers (cdump/radiacode CTRL).</summary>
[Flags]
public enum NotifyEvents : uint
{
    Buttons = 1 << 0,
    Clicks = 1 << 1,           // one click per detected particle (sound only)
    DoseRateAlarm1 = 1 << 2,
    DoseRateAlarm2 = 1 << 3,
    DoseRateOutOfScale = 1 << 4,
    DoseAlarm1 = 1 << 5,
    DoseAlarm2 = 1 << 6,
    DoseOutOfScale = 1 << 7,
    Connection = 1 << 8,       // device menu Signals → Connection (sound only on RC-110 FW 4.15)
    Power = 1 << 9,            // device menu Signals → Power
}

public enum DisplayDirection : uint { Auto = 0, Right = 1, Left = 2 }

/// <summary>
/// User-facing device settings in friendly units. Raw register units: dose rate µrem/h (×10 = nSv/h),
/// dose µrem (÷100 = µSv), count rate counts per 10 s (÷10 = cps). Seen on RC-110 firmware 4.15.
/// The sound/vibration control registers carry more bits than are known; unknown bits are preserved on write.
/// </summary>
public sealed record DeviceSettings
{
    public bool SoundOn { get; init; }
    public uint SoundCtrl { get; init; }
    public bool VibrationOn { get; init; }
    public uint VibroCtrl { get; init; }
    public uint Brightness { get; init; }            // 0–9
    public uint DisplayOffSeconds { get; init; }     // see OffTimes
    public DisplayDirection Direction { get; init; }
    public double DoseRateAlarm1NSvH { get; init; }
    public double DoseRateAlarm2NSvH { get; init; }
    public double CountRateAlarm1Cps { get; init; }
    public double CountRateAlarm2Cps { get; init; }
    public double DoseAlarm1MicroSv { get; init; }
    public double DoseAlarm2MicroSv { get; init; }

    // Optional registers (from the device's own register map, RC-110 FW 4.15); only written if the device reported them.
    public uint SoundVolume { get; init; } = 50;     // 0–100
    public bool English { get; init; } = true;      // DEVICE_LANG: 0 = English (RC-110 FW 4.15; cdump/radiacode has it reversed)
    public bool DoseInSievert { get; init; } = true; // DS_UNITS: 1 = Sv, 0 = R
    public bool NanoSievertPerHour { get; init; } = true; // USE_nSv_h: small dose rates in nSv/h
    public bool CountsPerMinute { get; init; }       // CR_UNITS: 1 = cpm, 0 = cps
    public bool Fahrenheit { get; init; }            // TEMP_UNITS: 1 = °F, 0 = °C
    public uint DeviceCtrl { get; init; } = 0x23;    // DEVICE_CTRL: bit 3 = LED (Signals → Light); other bits unknown, kept

    /// <summary>Registers the device actually reported; <see cref="ToRaw"/> only returns these (all, if empty).</summary>
    internal IReadOnlySet<Vsfr> Available { get; init; } = new HashSet<Vsfr>();

    public bool Has(Vsfr register) => Available.Count == 0 || Available.Contains(register);

    public bool ClickSound => (SoundCtrl & (uint)NotifyEvents.Clicks) != 0;
    public bool ButtonSound => (SoundCtrl & (uint)NotifyEvents.Buttons) != 0;
    public bool ButtonVibration => (VibroCtrl & (uint)NotifyEvents.Buttons) != 0;
    public bool ConnectionSound => (SoundCtrl & (uint)NotifyEvents.Connection) != 0;
    public bool PowerSound => (SoundCtrl & (uint)NotifyEvents.Power) != 0;
    public bool Led => (DeviceCtrl & LedBit) != 0;

    /// <summary>DEVICE_CTRL bit for the LED (device menu Signals → Light), found on RC-110 FW 4.15.</summary>
    public const uint LedBit = 1 << 3;

    /// <summary>Display auto-off choices in seconds, by register index (list from the device configuration).</summary>
    public static readonly uint[] OffTimes = [5, 10, 15, 30, 120, 300];

    /// <summary>Registers this record maps to, in read order.</summary>
    internal static readonly Vsfr[] Registers =
    [
        Vsfr.SoundOn, Vsfr.SoundCtrl, Vsfr.VibroOn, Vsfr.VibroCtrl, Vsfr.DisplayBrightness, Vsfr.DisplayOffTime,
        Vsfr.DisplayDirection, Vsfr.DoseRateLevel1, Vsfr.DoseRateLevel2, Vsfr.CountRateLevel1, Vsfr.CountRateLevel2,
        Vsfr.DoseLevel1, Vsfr.DoseLevel2,
    ];

    /// <summary>Registers read when present; missing ones are left out and never written.</summary>
    internal static readonly Vsfr[] OptionalRegisters =
    [
        // DEVICE_LANG is left out: the international RC-110 firmware (Language=English in its configuration) has only English.
        Vsfr.SoundVolume, Vsfr.DoseUnits, Vsfr.UseNanoSvH, Vsfr.CountRateUnits, Vsfr.TempUnits, Vsfr.DeviceCtrl,
    ];

    internal static DeviceSettings FromRaw(IReadOnlyDictionary<Vsfr, uint> r) => new()
    {
        SoundOn = r[Vsfr.SoundOn] != 0,
        SoundCtrl = r[Vsfr.SoundCtrl],
        VibrationOn = r[Vsfr.VibroOn] != 0,
        VibroCtrl = r[Vsfr.VibroCtrl],
        Brightness = r[Vsfr.DisplayBrightness],
        DisplayOffSeconds = r[Vsfr.DisplayOffTime] < OffTimes.Length ? OffTimes[r[Vsfr.DisplayOffTime]] : 10,
        Direction = (DisplayDirection)r[Vsfr.DisplayDirection],
        DoseRateAlarm1NSvH = r[Vsfr.DoseRateLevel1] * 10.0,
        DoseRateAlarm2NSvH = r[Vsfr.DoseRateLevel2] * 10.0,
        CountRateAlarm1Cps = r[Vsfr.CountRateLevel1] / 10.0,
        CountRateAlarm2Cps = r[Vsfr.CountRateLevel2] / 10.0,
        DoseAlarm1MicroSv = r[Vsfr.DoseLevel1] / 100.0,
        DoseAlarm2MicroSv = r[Vsfr.DoseLevel2] / 100.0,
        SoundVolume = r.GetValueOrDefault(Vsfr.SoundVolume, 50u),
        English = r.GetValueOrDefault(Vsfr.DeviceLang, 0u) == 0,
        DoseInSievert = r.GetValueOrDefault(Vsfr.DoseUnits, 1u) != 0,
        NanoSievertPerHour = r.GetValueOrDefault(Vsfr.UseNanoSvH, 1u) != 0,
        CountsPerMinute = r.GetValueOrDefault(Vsfr.CountRateUnits, 0u) != 0,
        Fahrenheit = r.GetValueOrDefault(Vsfr.TempUnits, 0u) != 0,
        DeviceCtrl = r.GetValueOrDefault(Vsfr.DeviceCtrl, 0x23u),
        Available = r.Keys.ToHashSet(),
    };

    internal IReadOnlyDictionary<Vsfr, uint> ToRaw()
    {
        int offIndex = Array.IndexOf(OffTimes, DisplayOffSeconds);
        var all = new Dictionary<Vsfr, uint>
        {
            [Vsfr.SoundOn] = SoundOn ? 1u : 0u,
            [Vsfr.SoundCtrl] = SoundCtrl,
            [Vsfr.VibroOn] = VibrationOn ? 1u : 0u,
            [Vsfr.VibroCtrl] = VibroCtrl & ~(uint)NotifyEvents.Clicks,
            [Vsfr.DisplayBrightness] = Math.Min(Brightness, 9u),
            [Vsfr.DisplayOffTime] = (uint)(offIndex < 0 ? 1 : offIndex),
            [Vsfr.DisplayDirection] = (uint)Direction,
            [Vsfr.DoseRateLevel1] = ToRaw(DoseRateAlarm1NSvH / 10.0),
            [Vsfr.DoseRateLevel2] = ToRaw(DoseRateAlarm2NSvH / 10.0),
            [Vsfr.CountRateLevel1] = ToRaw(CountRateAlarm1Cps * 10.0),
            [Vsfr.CountRateLevel2] = ToRaw(CountRateAlarm2Cps * 10.0),
            [Vsfr.DoseLevel1] = ToRaw(DoseAlarm1MicroSv * 100.0),
            [Vsfr.DoseLevel2] = ToRaw(DoseAlarm2MicroSv * 100.0),
            [Vsfr.SoundVolume] = Math.Min(SoundVolume, 100u),
            [Vsfr.DeviceLang] = English ? 0u : 1u,
            [Vsfr.DoseUnits] = DoseInSievert ? 1u : 0u,
            [Vsfr.UseNanoSvH] = NanoSievertPerHour ? 1u : 0u,
            [Vsfr.CountRateUnits] = CountsPerMinute ? 1u : 0u,
            [Vsfr.TempUnits] = Fahrenheit ? 1u : 0u,
            [Vsfr.DeviceCtrl] = DeviceCtrl,
        };
        return all.Where(kv => Has(kv.Key)).ToDictionary();
    }

    private static uint ToRaw(double v) => (uint)Math.Clamp(Math.Round(v), 0, 999_999_999);

    /// <summary>Sets or clears one event bit, keeping all other (including unknown) bits.</summary>
    public static uint WithFlag(uint ctrl, NotifyEvents flag, bool on) => on ? ctrl | (uint)flag : ctrl & ~(uint)flag;
}

/// <summary>Read-only device diagnostics (register map, RC-110 FW 4.15). Never written by the app.</summary>
public sealed record DeviceDiagnostics(IReadOnlyList<(string Name, string Value)> Items);