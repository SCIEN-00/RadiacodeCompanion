using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using Radiacode.App.Controls;
using Radiacode.Core;
using Radiacode.Transport.Ble;
using Radiacode.Transport.LibUsb;

namespace Radiacode.App.ViewModels;

/// <summary>A RadiaCode that can be connected: <paramref name="Key"/> is the USB key or the Bluetooth address.</summary>
public sealed record DeviceChoice(string Serial, bool Usb, string Key)
{
    public string Label => $"{Serial} · {(Usb ? "USB" : "Bluetooth")}";
}

/// <summary>One row of the "lines of interest" table on the Spectrum page.</summary>
public sealed record LineRow(string Nuclide, string Origin, string Energy, string Significance, string Verdict, double Opacity);

public partial class MainViewModel : ObservableObject
{
    private const int OverviewLength = 600;          // 10 min at 1 Hz
    private const int SessionLength = 24 * 3600;     // 24 h at 1 Hz
    private const int MaxChartPoints = 900;
    private static readonly int[] HistoryRangesMinutes = [10, 60, 360, 1440];

    private readonly List<(DateTime T, double V)> _dose = [], _count = [], _hardness = [];
    private readonly List<Sample> _session = [];
    private readonly HistoryLog _history = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private CancellationTokenSource? _cts;
    private RadiacodeDevice? _dev;
    private Spectrum? _spectrum;
    private Spectrum? _displaySpectrum;
    private GainFix? _gainFix;
    private BackgroundSpectrum? _background = BackgroundSpectrum.Load();
    private DateTime _nextSpectrum = DateTime.MinValue;
    private DateTime _nextHistoryRefresh = DateTime.MinValue;

    private readonly record struct Sample(DateTime Time, double DoseNSvH, double Cps);

    // Accumulated dose: the device only reports it in rare-data records (at connect), so we keep
    // integrating the live dose rate on top of the last reported value, like the device does.
    private double? _doseMicroSv;
    private TimeSpan _doseDuration;
    private DateTime _doseReportedAt;
    private DateTime? _lastRealTime;
    private double? _batteryPercent;
    private bool _charging;


    // ---- connection state -------------------------------------------------------------------
    [ObservableProperty] public partial string Status { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLive), nameof(PulseActive))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLive), nameof(PulseActive), nameof(PauseText))]
    public partial bool IsPaused { get; set; }

    /// <summary>Streaming data right now.</summary>
    public bool IsLive => IsConnected && !IsPaused;
    /// <summary>Drives the logo/ambient pulse.</summary>
    public bool PulseActive => IsLive && AnimationsEnabled;
    public string PauseText => IsPaused ? "Resume" : "Pause";

    /// <summary>Label of the single connect/disconnect button.</summary>
    [ObservableProperty] public partial string ConnectText { get; set; }
    /// <summary>Raised for every real-time reading (drives the status glow).</summary>
    public event Action? ReadingReceived;
    private bool _running;
    private string _liveText = "Live · USB";
    private DeviceChoice? _target;

    /// <summary>Set by the view: asks the user to pick one of several devices (null = cancelled).</summary>
    public Func<IReadOnlyList<DeviceChoice>, Task<DeviceChoice?>>? ChooseDevice { get; set; }

    [RelayCommand]
    private void ToggleConnection()
    {
        if (_running) _cts?.Cancel(); else _ = ConnectAsync();
    }

    /// <summary>Logo/ambient pulse speed: 1.0 at the configured background count rate, faster with more counts.</summary>
    [ObservableProperty] public partial double PulseSpeed { get; set; }

    // ---- live values --------------------------------------------------------------------------
    [ObservableProperty] public partial string Serial { get; set; }
    [ObservableProperty] public partial string DoseRateText { get; set; }
    [ObservableProperty] public partial string DoseRateErrorText { get; set; }
    [ObservableProperty] public partial string DoseRateState { get; set; }
    [ObservableProperty] public partial string CountRateText { get; set; }
    [ObservableProperty] public partial string CountRateErrorText { get; set; }
    [ObservableProperty] public partial string HardnessText { get; set; }
    [ObservableProperty] public partial string DoseText { get; set; }
    [ObservableProperty] public partial string DoseDurationText { get; set; }
    [ObservableProperty] public partial string BatteryText { get; set; }
    [ObservableProperty] public partial string TemperatureText { get; set; }
    [ObservableProperty] public partial string LastReadingText { get; set; }

    [ObservableProperty] public partial IReadOnlyList<double>? DoseHistory { get; set; }
    [ObservableProperty] public partial IReadOnlyList<double>? CountHistory { get; set; }
    [ObservableProperty] public partial IReadOnlyList<double>? HardnessHistory { get; set; }
    [ObservableProperty] public partial Func<int, double>? DoseX { get; set; }
    [ObservableProperty] public partial Func<int, double>? CountX { get; set; }
    [ObservableProperty] public partial Func<int, double>? HardnessX { get; set; }
    /// <summary>X labels for the 10-minute charts: values are minutes before now.</summary>
    public Func<double, string> MinutesAgoFormat { get; } = v => Math.Abs(v) < 1e-9 ? "now" : Math.Abs(v) < 1 ? $"{v * 60:0} s" : $"{v:0.#} min";

    // ---- spectrum -----------------------------------------------------------------------------
    [ObservableProperty] public partial IReadOnlyList<double>? SpectrumCounts { get; set; }
    [ObservableProperty] public partial Func<int, double>? SpectrumX { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ChartMarker>? SpectrumMarkers { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ChartMarker>? SpectrumMarkersDetected { get; set; }
    [ObservableProperty] public partial string SpectrumInfo { get; set; }
    [ObservableProperty] public partial bool SpectrumLog { get; set; }
    [ObservableProperty] public partial string SpectrumExportText { get; set; }
    [ObservableProperty] public partial string CalibrationText { get; set; }
    [ObservableProperty] public partial IReadOnlyList<double>? SpectrumOverlay { get; set; }
    [ObservableProperty] public partial bool ShowBackground { get; set; }
    [ObservableProperty] public partial string BackgroundCompareText { get; set; }
    [ObservableProperty] public partial bool GainCorrectionEnabled { get; set; }

    // ---- alarm ---------------------------------------------------------------------------------
    [ObservableProperty] public partial bool AlarmEnabled { get; set; }
    [ObservableProperty] public partial double AlarmLevel { get; set; }
    [ObservableProperty] public partial bool AlarmNotifications { get; set; }
    [ObservableProperty] public partial bool AlarmActive { get; set; }
    [ObservableProperty] public partial string AlarmText { get; set; }
    private int _aboveCount, _belowCount;
    private double _alarmPeak;
    private DateTime _alarmSince;
    private const int AlarmRaiseReadings = 5;   // ~5 s above the level before alarming
    private const int AlarmClearReadings = 10;  // ~10 s below 80 % of the level before clearing
    public Func<double, string> KeVFormat { get; } = v => $"{v:0} keV";
    public ObservableCollection<LineRow> SpectrumLines { get; } = [];

    // ---- history ------------------------------------------------------------------------------
    [ObservableProperty] public partial int HistoryRangeIndex { get; set; }
    [ObservableProperty] public partial IReadOnlyList<double>? HistoryDose { get; set; }
    [ObservableProperty] public partial IReadOnlyList<double>? HistoryCount { get; set; }
    [ObservableProperty] public partial Func<int, double>? HistoryX { get; set; }
    [ObservableProperty] public partial Func<double, string>? HistoryXFormat { get; set; }
    [ObservableProperty] public partial string HistoryInfo { get; set; }
    [ObservableProperty] public partial string HistoryDoseStats { get; set; }
    [ObservableProperty] public partial string HistoryCountStats { get; set; }
    [ObservableProperty] public partial string HistoryExportText { get; set; }

    // ---- events -------------------------------------------------------------------------------
    public ObservableCollection<string> Events { get; } = [];
    public ObservableCollection<string> RecentEvents { get; } = [];
    public bool HasNoEvents => Events.Count == 0;

    // ---- device -------------------------------------------------------------------------------
    [ObservableProperty] public partial string FirmwareText { get; set; }
    [ObservableProperty] public partial string FirmwareUpdateText { get; set; }
    [ObservableProperty] public partial bool FirmwareUpdateAvailable { get; set; }
    public string FirmwareDownloadsUrl => FirmwareCatalog.DownloadsUrl;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    [ObservableProperty] public partial string SpectrumFormatText { get; set; }
    [ObservableProperty] public partial string TransportText { get; set; }

    // ---- settings -----------------------------------------------------------------------------
    [ObservableProperty] public partial int ThemeIndex { get; set; }
    [ObservableProperty] public partial int ConnectionIndex { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PulseActive))]
    public partial bool AnimationsEnabled { get; set; }

    [ObservableProperty] public partial double BackgroundCps { get; set; }

    public static string ExportFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Radiacode Companion");
    public string ExportFolderText => ExportFolder;
    public string SettingsFileText => AppSettings.FilePath;
    public string HistoryFolderText => HistoryLog.Folder;
    public string VersionText { get; } =
        typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(MainViewModel).Assembly.GetName().Version?.ToString() ?? "dev";

    public MainViewModel()
    {
        Status = "Not connected";
        ConnectText = "Connect";
        Serial = "—";
        DoseRateText = "—";
        DoseRateErrorText = "";
        DoseRateState = "Waiting for data";
        CountRateText = "—";
        CountRateErrorText = "";
        HardnessText = "—";
        DoseText = "—";
        DoseDurationText = "";
        BatteryText = "—";
        TemperatureText = "—";
        LastReadingText = "";
        PulseSpeed = 1.0;
        SpectrumInfo = "No spectrum yet";
        SpectrumLog = true;
        SpectrumExportText = "";
        CalibrationText = "—";
        BackgroundCompareText = "";
        GainCorrectionEnabled = _settings.GainCorrection;
        AlarmEnabled = _settings.AlarmEnabled;
        AlarmLevel = _settings.AlarmLevel;
        AlarmNotifications = _settings.AlarmNotifications;
        AlarmText = "";
        HistoryInfo = "History starts when a device is connected.";
        HistoryDoseStats = "";
        HistoryCountStats = "";
        HistoryExportText = "";
        FirmwareText = "—";
        FirmwareUpdateText = "Checked after connecting.";
        SpectrumFormatText = "—";
        TransportText = "—";

        ThemeIndex = _settings.Theme;
        ConnectionIndex = _settings.Connection;
        AnimationsEnabled = _settings.Animations;
        BackgroundCps = _settings.BackgroundCps;

        // Restore the last 24 h of readings saved by earlier sessions.
        HistoryLog.RemoveOld();
        foreach (var (t, dose, cps) in HistoryLog.LoadSince(DateTime.Now.AddHours(-24)))
            _session.Add(new Sample(t, dose, cps));
        if (_session.Count > 0) RefreshHistory();
    }

    /// <summary>Writes any buffered readings to disk (call on app exit).</summary>
    public void FlushHistory() => _history.Flush();

    partial void OnIsPausedChanged(bool value)
    {
        if (!IsConnected) return;
        Status = value ? "Paused" : _liveText;
        AddEvent(value ? "Paused" : "Resumed");
    }

    partial void OnConnectionIndexChanged(int value) { _settings.Connection = value; _settings.Save(); }
    partial void OnThemeIndexChanged(int value) { _settings.Theme = value; _settings.Save(); }
    partial void OnAnimationsEnabledChanged(bool value) { _settings.Animations = value; _settings.Save(); }

    partial void OnBackgroundCpsChanged(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return;
        _settings.BackgroundCps = value;
        _settings.Save();
    }

    partial void OnHistoryRangeIndexChanged(int value) => RefreshHistory();

    // ---- device selection ---------------------------------------------------------------------
    /// <summary>
    /// Finds connectable devices (USB first; Bluetooth when nothing is plugged in or in Bluetooth-only mode).
    /// One device → used directly; several → the last used one, otherwise the user picks.
    /// </summary>
    private async Task<DeviceChoice?> SelectDeviceAsync(CancellationToken ct)
    {
        var candidates = new List<DeviceChoice>();
        if (ConnectionIndex != 2)
        {
            try
            {
                candidates.AddRange(LibUsbTransport.List().Select(u => new DeviceChoice(u.UsbSerial ?? u.Key, true, u.Key)));
            }
            catch (Exception ex)
            {
                AddEvent($"USB not available: {ex.Message}");
            }
        }
        if (ConnectionIndex != 1 && candidates.Count == 0)
        {
            Status = "Searching Bluetooth…";
            foreach (var b in await BleTransport.ScanAsync(TimeSpan.FromSeconds(6), ct))
                candidates.Add(new DeviceChoice(b.Serial ?? b.Name, false, b.Id));
        }

        if (candidates.Count == 0)
            throw new DeviceNotFoundException(ConnectionIndex switch
            {
                1 => "No RadiaCode on USB. Is the cable connected and the WinUSB driver (Windows) / udev rule (Linux) installed?",
                2 => "No RadiaCode found over Bluetooth. Is it switched on, in range, and not connected to another app or Bluetooth proxy?",
                _ => "No RadiaCode found on USB or Bluetooth. Is it switched on and in range, and not connected to another app or Bluetooth proxy?",
            });
        if (candidates.Count == 1) return candidates[0];
        if (candidates.FirstOrDefault(c => c.Serial == _settings.LastDevice) is { } last) return last;
        return ChooseDevice is { } choose ? await choose(candidates) : candidates[0];
    }

    // ---- connection loop ----------------------------------------------------------------------
    /// <summary>
    /// Connects and streams until the user disconnects. If an established connection drops (cable pulled, out of
    /// Bluetooth range), it keeps retrying every few seconds; a failure on the very first attempt is reported instead.
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_running) return;
        _running = true;
        ConnectText = "Disconnect";
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        bool everConnected = false;
        int attempt = 0;
        try
        {
            Status = "Looking for devices…";
            _target = await SelectDeviceAsync(ct);
            if (_target is null)
            {
                Status = "Not connected";
                return;
            }
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RunSessionAsync(ct, () => everConnected = true);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }

                catch (Exception ex)
                {
                    IsConnected = false;
                    _dev = null;
                    _history.Flush();
                    if (!everConnected)
                    {
                        Status = ex.Message;
                        AddEvent($"Connection error: {ex.Message}");
                        return;
                    }
                    attempt++;
                    if (attempt == 1) AddEvent($"Connection lost ({ex.Message}); reconnecting…");
                    Status = $"Reconnecting… (attempt {attempt})";
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(5 * attempt, 30)), ct);
                    continue;
                }
            }
            Status = "Disconnected";
            AddEvent("Disconnected");
        }
        catch (OperationCanceledException)
        {
            Status = "Disconnected";
            AddEvent("Disconnected");
        }
        catch (Exception ex)
        {
            // Device search failed before any session started.
            Status = ex.Message;
            AddEvent($"Connection error: {ex.Message}");
        }
        finally
        {
            _history.Flush();
            _dev = null;
            _running = false;
            ConnectText = "Connect";
            IsConnected = false;
        }
    }

    private async Task RunSessionAsync(CancellationToken ct, Action onConnected)
    {
        Status = "Connecting…";
        var target = _target ?? throw new InvalidOperationException("No device selected.");
        ITransport transport;
        if (target.Usb)
        {
            try
            {
                transport = LibUsbTransport.Open(target.Key);
                _liveText = "Live · USB";
            }
            catch (DeviceNotFoundException) when (ConnectionIndex == 0)
            {
                // Cable unplugged: in automatic mode carry on over Bluetooth with the same device.
                Status = "Searching Bluetooth…";
                var ble = (await BleTransport.ScanAsync(TimeSpan.FromSeconds(8), ct)).FirstOrDefault(b => b.Serial == target.Serial)
                          ?? throw new DeviceNotFoundException($"{target.Serial} is not on USB and not visible over Bluetooth.");
                transport = await BleTransport.ConnectAsync(TimeSpan.FromSeconds(20), ct, ble.Id);
                _liveText = "Live · Bluetooth";
            }
        }
        else
        {
            Status = "Connecting over Bluetooth…";
            transport = await BleTransport.ConnectAsync(TimeSpan.FromSeconds(20), ct, target.Key);
            _liveText = "Live · Bluetooth";
        }        await using var dev = await RadiacodeDevice.ConnectAsync(transport, ct);
        _dev = dev;
        Serial = await dev.GetSerialNumberAsync(ct);
        FirmwareText = dev.Firmware?.ToString() ?? "—";
        SpectrumFormatText = $"v{dev.SpectrumFormatVersion}";
        TransportText = transport.Description;
        IsPaused = false;
        IsConnected = true;
        Status = _liveText;
        AddEvent($"Connected to {Serial} ({transport.Description}), firmware {FirmwareText}");
        onConnected();
        if (_settings.LastDevice != Serial) { _settings.LastDevice = Serial; _settings.Save(); }
        if (dev.Firmware is { } fw && !FirmwareUpdateText.StartsWith("Up to date", StringComparison.Ordinal)) _ = CheckFirmwareAsync(fw, Serial);

        // Backlog: skip old real-time values, but keep the latest status (dose, battery, temperature).
        if ((await dev.ReadDataBufAsync(ct)).OfType<RareData>().LastOrDefault() is { } rare) Apply(rare);
        await ReadStatusAsync(dev, ct);

        _nextSpectrum = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            if (IsPaused) continue;
            foreach (var rec in await dev.ReadDataBufAsync(ct)) Apply(rec);
            if (DateTime.Now >= _nextSpectrum)
            {
                UpdateSpectrum(await dev.ReadSpectrumAsync(ct));
                _nextSpectrum = DateTime.Now.AddSeconds(10);
                await ReadStatusAsync(dev, ct);
            }
            if (DateTime.Now >= _nextHistoryRefresh) RefreshHistory();
        }
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Compares the device firmware with the newest one RadiaCode has announced for this model.</summary>
    private async Task CheckFirmwareAsync(FirmwareVersion fw, string serial)
    {
        if (FirmwareCatalog.ModelOf(serial) is not { } model) { FirmwareUpdateText = "Unknown model, cannot check."; return; }
        FirmwareUpdateText = "Checking radiacode.com…";
        try
        {
            var html = await Http.GetStringAsync(FirmwareCatalog.ChangelogUrl);
            if (FirmwareCatalog.LatestFor(html, model) is not { } latest)
            {
                FirmwareUpdateText = $"No firmware for RC-{model} listed in the changelog.";
                return;
            }
            var current = (fw.TargetMajor, fw.TargetMinor);
            string latestText = $"{latest.Major}.{latest.Minor:00}";
            FirmwareUpdateAvailable = latest.CompareTo(current) > 0;
            FirmwareUpdateText = FirmwareUpdateAvailable
                ? $"Firmware {latestText} is available. Install it with the official RadiaCode app (USB or Bluetooth)."
                : $"Up to date (newest listed: {latestText}, checked {DateTime.Now:yyyy-MM-dd HH:mm}).";
            if (FirmwareUpdateAvailable) AddEvent($"Firmware {latestText} available (device has {fw.TargetMajor}.{fw.TargetMinor:00})");
        }
        catch (Exception ex)
        {
            FirmwareUpdateText = $"Could not check ({ex.GetType().Name}). Offline?";
        }
    }

    [RelayCommand]
    private void Disconnect() => _cts?.Cancel();

    [RelayCommand]
    private void TogglePause()
    {
        if (IsConnected) IsPaused = !IsPaused;
    }

    /// <summary>Temperature can be read directly; battery % only arrives with rare data.</summary>
    private async Task ReadStatusAsync(RadiacodeDevice dev, CancellationToken ct)
    {
        if (await dev.ReadTemperatureAsync(ct) is { } t) TemperatureText = $"{t:F1} °C";

    }

    private void UpdateBatteryText() =>
        BatteryText = (_batteryPercent is { } p ? $"{p:F0} %" : "—") + (_charging ? " · charging" : "");

    private void UpdateDoseText(DateTime now)
    {
        if (_doseMicroSv is not { } dose) return;
        DoseText = dose.ToString(dose < 100 ? "F3" : "F2");
        var span = _doseDuration + (now - _doseReportedAt);
        DoseDurationText = $"over {(int)span.TotalDays} d {span.Hours} h {span.Minutes} min";
    }

    private void Apply(DataBufRecord rec)
    {
        switch (rec)
        {
            case RealTimeData rt:
                DoseRateText = rt.DoseRateNanoSvH.ToString("F1");
                DoseRateErrorText = $"± {rt.DoseRateErrorPercent:F0} % statistical error";
                CountRateText = rt.CountRate.ToString("F2");
                CountRateErrorText = $"± {rt.CountRateErrorPercent:F1} % statistical error";
                HardnessText = rt.Hardness?.ToString("F2") ?? "—";
                double level = AlarmLevel > 0 ? AlarmLevel : 300;
                DoseRateState = rt.DoseRateNanoSvH < level / 2 ? "Normal background" : rt.DoseRateNanoSvH < level ? "Elevated" : "Above alarm level";
                CheckAlarm(rt.DoseRateNanoSvH);
                LastReadingText = rt.Timestamp.ToString("HH:mm:ss");
                if (rt.IsCharging != _charging) { _charging = rt.IsCharging; UpdateBatteryText(); }
                ReadingReceived?.Invoke();
                var now = DateTime.Now;
                (DoseX, DoseHistory) = Push(_dose, now, rt.DoseRateNanoSvH); // X first: the chart redraws on each assignment
                (CountX, CountHistory) = Push(_count, now, rt.CountRate);
                if (rt.Hardness is { } hd) (HardnessX, HardnessHistory) = Push(_hardness, now, hd);

                _session.Add(new Sample(DateTime.Now, rt.DoseRateNanoSvH, rt.CountRate));
                _history.Append(DateTime.Now, rt.DoseRateNanoSvH, rt.CountRate);
                if (_session.Count > SessionLength) _session.RemoveRange(0, _session.Count - SessionLength);

                double reference = BackgroundCps > 0 ? BackgroundCps : 9.0;
                var target = Math.Clamp(Math.Sqrt(rt.CountRate / reference), 0.5, 4.0);
                PulseSpeed += (target - PulseSpeed) * 0.3; // smooth out statistical jitter

                if (_doseMicroSv is not null && _lastRealTime is { } prev)
                {
                    var dt = Math.Clamp((rt.Timestamp - prev).TotalHours, 0, 30.0 / 3600); // ignore gaps > 30 s
                    _doseMicroSv += rt.DoseRateMicroSvH * dt;
                    UpdateDoseText(DateTime.Now);
                }
                _lastRealTime = rt.Timestamp;
                break;

            case RareData rd:
                _doseMicroSv = rd.DoseMicroSv;
                _doseDuration = TimeSpan.FromSeconds(rd.DurationSeconds);
                _doseReportedAt = DateTime.Now;
                UpdateDoseText(DateTime.Now);
                _batteryPercent = rd.ChargePercent;
                UpdateBatteryText();
                TemperatureText = $"{rd.TemperatureC:F1} °C";
                break;

            case DeviceEvent ev:
                AddEvent($"Device: {ev.Event}");
                if (AlarmNotifications && ev.Event.ToString().Contains("Alarm", StringComparison.Ordinal))
                    Notifier.Show("RadiaCode alarm", $"{Serial}: {ev.Event}", urgent: true);
                break;
        }
    }

    /// <summary>Appends a reading (keeps the last 10 min) and returns the values plus an index → minutes-ago mapping.</summary>
    private static (Func<int, double> X, double[] Values) Push(List<(DateTime T, double V)> buffer, DateTime now, double v)
    {
        buffer.Add((now, v));
        if (buffer.Count > OverviewLength) buffer.RemoveRange(0, buffer.Count - OverviewLength);
        var times = buffer.Select(p => p.T).ToArray();
        return (i => (times[Math.Clamp(i, 0, times.Length - 1)] - now).TotalMinutes, buffer.Select(p => p.V).ToArray());
    }

    // ---- spectrum -----------------------------------------------------------------------------
    private void UpdateSpectrum(Spectrum raw)
    {
        _spectrum = raw;

        // Self-calibration on K-40 (temperature drift of the scintillator gain).
        var sp = raw;
        _gainFix = null;
        if (GainCorrectionEnabled && GainCorrection.FindK40(raw) is { } fix && Math.Abs(fix.Factor - 1) < 0.1)
        {
            _gainFix = fix;
            sp = fix.Apply(raw);
        }
        _displaySpectrum = sp;

        SpectrumX = ch => sp.EnergyKeV(ch);
        SpectrumCounts = sp.Counts.Select(c => (double)c).ToArray();
        UpdateBackgroundOverlay();
        var d = sp.Duration;
        SpectrumInfo = $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00} · {sp.TotalCounts:N0} counts · " +
                       $"{sp.AverageCountRate:F2} cps · {sp.EnergyKeV(0):F0}–{sp.EnergyKeV(sp.Counts.Length - 1):F0} keV";
        CalibrationText = $"E = {raw.A0:F2} + {raw.A1:F4}·ch + {raw.A2:0.###E+0}·ch²  keV" + (_gainFix is { } g
            ? $" · K-40 found at {g.FoundKeV:F0} keV → gain ×{g.Factor:F3} applied"
            : GainCorrectionEnabled ? " · no clear K-40 peak yet, gain not corrected" : "");

        var results = SpectrumAnalysis.AnalyzeAll(sp);
        SpectrumLines.Clear();
        foreach (var r in results)
        {
            string verdict = !r.InRange ? "out of range"
                : r.Detected ? "likely present"
                : r.Note is not null ? "unconfirmed"
                : r.Significance >= 1.5 ? "hint"
                : "not seen";
            SpectrumLines.Add(new LineRow(r.Line.Nuclide, r.Note ?? r.Line.Origin, $"{r.Line.EnergyKeV:0.#} keV",
                r.InRange ? $"{r.Significance:0.0} σ" : "—", verdict, r.Detected ? 1.0 : 0.55));
        }
        SpectrumMarkers = results.Where(r => r.InRange)
            .Select(r => new ChartMarker(r.Line.EnergyKeV, r.Significance >= 1.5 ? $"{r.Line.Nuclide}\n{r.Significance:0.0} σ" : r.Line.Nuclide, r.Detected)).ToArray();
        SpectrumMarkersDetected = results.Where(r => r.Detected)
            .Select(r => new ChartMarker(r.Line.EnergyKeV, $"{r.Line.Nuclide}\n{r.Significance:0.0} σ", true)).ToArray();
    }

    // ---- alarm ---------------------------------------------------------------------------------
    private void CheckAlarm(double nSvH)
    {
        if (!AlarmEnabled || AlarmLevel <= 0)
        {
            if (AlarmActive) ClearAlarm();
            return;
        }
        if (nSvH >= AlarmLevel)
        {
            _aboveCount++;
            _belowCount = 0;
            _alarmPeak = Math.Max(_alarmPeak, nSvH);
            if (!AlarmActive && _aboveCount >= AlarmRaiseReadings)
            {
                AlarmActive = true;
                _alarmSince = DateTime.Now;
                AddEvent($"ALARM: dose rate {nSvH:F0} nSv/h above {AlarmLevel:F0} nSv/h");
                if (AlarmNotifications)
                    Notifier.Show("Dose rate alarm", $"{nSvH:F0} nSv/h (alarm level {AlarmLevel:F0} nSv/h)", urgent: true);
            }
        }
        else
        {
            _aboveCount = 0;
            if (nSvH < AlarmLevel * 0.8) _belowCount++;
            if (AlarmActive && _belowCount >= AlarmClearReadings) ClearAlarm();
        }
        if (AlarmActive)
            AlarmText = $"Dose rate above {AlarmLevel:F0} nSv/h since {_alarmSince:HH:mm:ss}. Now {nSvH:F0} nSv/h, peak {_alarmPeak:F0} nSv/h.";
        else if (_aboveCount == 0) _alarmPeak = 0;
    }

    private void ClearAlarm()
    {
        AlarmActive = false;
        AddEvent($"Alarm cleared (peak {_alarmPeak:F0} nSv/h)");
        if (AlarmNotifications) Notifier.Show("Dose rate back to normal", $"Peak was {_alarmPeak:F0} nSv/h", urgent: false);
        _alarmPeak = 0;
        _aboveCount = _belowCount = 0;
    }

    partial void OnAlarmEnabledChanged(bool value) { _settings.AlarmEnabled = value; _settings.Save(); }
    partial void OnAlarmNotificationsChanged(bool value) { _settings.AlarmNotifications = value; _settings.Save(); }
    partial void OnAlarmLevelChanged(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return;
        _settings.AlarmLevel = value;
        _settings.Save();
    }

    [RelayCommand]
    private void TestNotification()
    {
        Notifier.Show("Radiacode Companion", "Test notification — alarms will look like this.", urgent: false);
        AddEvent(Notifier.LastError is { } err ? $"Test notification failed: {err}" : "Test notification sent");
    }

    // ---- background comparison ----------------------------------------------------------------
    private void UpdateBackgroundOverlay()
    {
        var sp = _displaySpectrum;
        if (!ShowBackground || _background is not { } bg || sp is null || bg.Counts.Length != sp.Counts.Length || bg.DurationSeconds <= 0)
        {
            SpectrumOverlay = null;
            BackgroundCompareText = _background is { } b
                ? $"Background saved {b.Saved:yyyy-MM-dd HH:mm} ({TimeSpan.FromSeconds(b.DurationSeconds).TotalHours:0.#} h, {b.TotalCounts:N0} counts)."
                : "No background saved yet. Measure a quiet place for an hour or more, then save it.";
            return;
        }
        double k = sp.Duration.TotalSeconds / bg.DurationSeconds;
        SpectrumOverlay = bg.Counts.Select(c => c * k).ToArray();
        double n = sp.TotalCounts, expected = bg.TotalCounts * k;
        double sigma = (n - expected) / Math.Sqrt(Math.Max(1, n + k * k * bg.TotalCounts));
        double pct = Math.Round(expected > 0 ? (n / expected - 1) * 100 : 0, 1);
        sigma = Math.Round(sigma, 1);
        BackgroundCompareText = $"Grey line = background from {bg.Saved:yyyy-MM-dd HH:mm}, scaled to this measuring time. " +
                                $"Total counts {pct:+0.0;-0.0;0.0} % vs background ({sigma:+0.0;-0.0;0.0} σ).";
    }

    partial void OnShowBackgroundChanged(bool value) => UpdateBackgroundOverlay();

    partial void OnGainCorrectionEnabledChanged(bool value)
    {
        _settings.GainCorrection = value;
        _settings.Save();
        if (_spectrum is { } s) UpdateSpectrum(s);
    }

    [RelayCommand]
    private void SaveBackground()
    {
        if (_spectrum is not { } sp) { BackgroundCompareText = "No spectrum to save yet."; return; }
        try
        {
            _background = BackgroundSpectrum.From(sp, Serial);
            _background.Save();
            ShowBackground = true;
            UpdateBackgroundOverlay();
            AddEvent($"Background saved ({sp.Duration.TotalHours:0.#} h, {sp.TotalCounts:N0} counts)");
        }
        catch (Exception ex) { BackgroundCompareText = $"Could not save background: {ex.Message}"; }
    }
    [RelayCommand]
    private async Task ResetSpectrumAsync()
    {
        if (_dev is not { } dev) return;
        try
        {
            await dev.ResetSpectrumAsync();
            _nextSpectrum = DateTime.MinValue;
            AddEvent("Spectrum reset");
        }
        catch (Exception ex) { AddEvent($"Spectrum reset failed: {ex.Message}"); }
    }

    [RelayCommand]
    private async Task ResetDoseAsync()
    {
        if (_dev is not { } dev) return;
        try
        {
            await dev.ResetDoseAsync();
            _doseMicroSv = 0;
            _doseDuration = TimeSpan.Zero;
            _doseReportedAt = DateTime.Now;
            UpdateDoseText(DateTime.Now);
            AddEvent("Accumulated dose reset");
        }
        catch (Exception ex) { AddEvent($"Dose reset failed: {ex.Message}"); }
    }

    [RelayCommand]
    private void ExportSpectrum()
    {
        if (_spectrum is not { } sp) { SpectrumExportText = "No spectrum to export yet."; return; }
        try
        {
            var path = NewExportPath("spectrum");
            var sb = new StringBuilder();
            sb.AppendLine($"# Radiacode Companion spectrum export, device {Serial}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(FormattableString.Invariant($"# duration_s={sp.Duration.TotalSeconds:F0}, a0={sp.A0}, a1={sp.A1}, a2={sp.A2}"));
            var corrected = _gainFix?.Apply(sp) ?? sp;
            if (_gainFix is { } g) sb.AppendLine(FormattableString.Invariant($"# K-40 gain correction x{g.Factor:F4} (found at {g.FoundKeV:F1} keV)"));
            sb.AppendLine("channel,energy_keV,energy_corrected_keV,counts");
            for (int i = 0; i < sp.Counts.Length; i++)
                sb.AppendLine(FormattableString.Invariant($"{i},{sp.EnergyKeV(i):F2},{corrected.EnergyKeV(i):F2},{sp.Counts[i]}"));
            File.WriteAllText(path, sb.ToString());
            SpectrumExportText = $"Saved {path}";
            AddEvent($"Spectrum exported: {Path.GetFileName(path)}");
        }
        catch (Exception ex) { SpectrumExportText = $"Export failed: {ex.Message}"; }
    }

    // ---- history ------------------------------------------------------------------------------
    private void RefreshHistory()
    {
        _nextHistoryRefresh = DateTime.Now.AddSeconds(5);
        int minutes = HistoryRangesMinutes[Math.Clamp(HistoryRangeIndex, 0, HistoryRangesMinutes.Length - 1)];
        var now = DateTime.Now;
        var from = now.AddMinutes(-minutes);
        int start = _session.FindIndex(s => s.Time >= from);
        if (start < 0 || _session.Count - start < 2)
        {
            HistoryDose = null;
            HistoryCount = null;
            HistoryDoseStats = HistoryCountStats = "";
            HistoryInfo = _session.Count == 0 ? "No readings yet. History is saved automatically while connected." : "Not enough data in this range yet.";
            return;
        }

        int n = _session.Count - start;
        int k = (int)Math.Ceiling(n / (double)MaxChartPoints);
        int m = (n + k - 1) / k;
        var dose = new double[m];
        var cps = new double[m];
        var times = new DateTime[m];
        for (int b = 0; b < m; b++)
        {
            int i0 = start + b * k, i1 = Math.Min(start + (b + 1) * k, _session.Count);
            double sd = 0, sc = 0;
            for (int i = i0; i < i1; i++) { sd += _session[i].DoseNSvH; sc += _session[i].Cps; }
            dose[b] = sd / (i1 - i0);
            cps[b] = sc / (i1 - i0);
            times[b] = _session[i0 + (i1 - i0) / 2].Time;
        }

        // Pick the axis unit from the span actually covered, so short sessions don't read "-0 min" everywhere.
        var span = now - times[0];
        double unitSeconds = span.TotalMinutes < 3 ? 1 : span.TotalHours < 3 ? 60 : 3600;
        string unit = unitSeconds == 1 ? "s" : unitSeconds == 60 ? "min" : "h";
        HistoryX = i => (times[i] - now).TotalSeconds / unitSeconds;
        HistoryXFormat = v => Math.Abs(v) < 1e-9 ? "now" : $"{v:0.#} {unit}";
        HistoryDose = dose;
        HistoryCount = cps;

        var raw = _session.GetRange(start, n);
        HistoryDoseStats = Stats(raw.Select(s => s.DoseNSvH), "nSv/h", "F1");
        HistoryCountStats = Stats(raw.Select(s => s.Cps), "cps", "F2");
        HistoryInfo = $"{n:N0} readings since {_session[start].Time:HH:mm}" + (k > 1 ? $" · chart averaged over {k} s" : "");
    }

    private static string Stats(IEnumerable<double> values, string unit, string format)
    {
        var a = values.ToArray();
        return $"min {a.Min().ToString(format)} · avg {a.Average().ToString(format)} · max {a.Max().ToString(format)} {unit}";
    }

    [RelayCommand]
    private void ExportHistory()
    {
        if (_session.Count == 0) { HistoryExportText = "No readings to export yet."; return; }
        try
        {
            var path = NewExportPath("history");
            var sb = new StringBuilder("time,dose_rate_nSv_h,count_rate_cps\n");
            foreach (var s in _session)
                sb.AppendLine(FormattableString.Invariant($"{s.Time:yyyy-MM-ddTHH:mm:ss},{s.DoseNSvH:F2},{s.Cps:F3}"));
            File.WriteAllText(path, sb.ToString());
            HistoryExportText = $"Saved {path}";
            AddEvent($"History exported: {Path.GetFileName(path)}");
        }
        catch (Exception ex) { HistoryExportText = $"Export failed: {ex.Message}"; }
    }

    private static string NewExportPath(string kind)
    {
        Directory.CreateDirectory(ExportFolder);
        return Path.Combine(ExportFolder, $"{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
    }

    // ---- events -------------------------------------------------------------------------------
    private void AddEvent(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {text}";
        Events.Insert(0, line);
        while (Events.Count > 500) Events.RemoveAt(Events.Count - 1);
        RecentEvents.Insert(0, line);
        while (RecentEvents.Count > 5) RecentEvents.RemoveAt(RecentEvents.Count - 1);
        OnPropertyChanged(nameof(HasNoEvents));
    }

    [RelayCommand]
    private void ClearEvents()
    {
        Events.Clear();
        RecentEvents.Clear();
        OnPropertyChanged(nameof(HasNoEvents));
    }
}
