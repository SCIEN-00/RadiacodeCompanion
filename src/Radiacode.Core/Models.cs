namespace Radiacode.Core;

/// <summary>Unit helpers. The device reports dose rate in R/h and dose in R.</summary>
public static class Units
{
    /// <summary>1 R ≈ 10 mSv (the convention used by the device firmware and vendor apps).</summary>
    public const double RoentgenToMicroSievert = 10_000.0;
}

public abstract record DataBufRecord(DateTime Timestamp);

/// <summary>Real-time measurement (about once per second).</summary>
public sealed record RealTimeData(
    DateTime Timestamp,
    float CountRate,
    float CountRateErrorPercent,
    float DoseRateRaw,
    float DoseRateErrorPercent,
    ushort Flags,
    byte RealTimeFlags) : DataBufRecord(Timestamp)
{
    /// <summary>Status bit 0x4000: set while the battery is charging (observed on RC-110 FW 4.15 together with ChargeStart/ChargeStop events).</summary>
    public bool IsCharging => (Flags & 0x4000) != 0;

    public double DoseRateMicroSvH => DoseRateRaw * Units.RoentgenToMicroSievert;
    public double DoseRateNanoSvH => DoseRateMicroSvH * 1000.0;

    /// <summary>RadiaCode "hardness": (µrem/h) / cps.</summary>
    public double? Hardness => CountRate > 0 ? DoseRateMicroSvH * 100.0 / CountRate : null;
}

public sealed record RawData(DateTime Timestamp, float CountRate, float DoseRateRaw) : DataBufRecord(Timestamp)
{
    public double DoseRateMicroSvH => DoseRateRaw * Units.RoentgenToMicroSievert;
}

public sealed record DoseRateDb(
    DateTime Timestamp, uint Count, float CountRate, float DoseRateRaw, float DoseRateErrorPercent, ushort Flags)
    : DataBufRecord(Timestamp)
{
    public double DoseRateMicroSvH => DoseRateRaw * Units.RoentgenToMicroSievert;
}

/// <summary>Slow-changing device state (accumulated dose, temperature, battery).</summary>
public sealed record RareData(
    DateTime Timestamp, uint DurationSeconds, float DoseRaw, float TemperatureC, float ChargePercent, ushort Flags)
    : DataBufRecord(Timestamp)
{
    public double DoseMicroSv => DoseRaw * Units.RoentgenToMicroSievert;
}

public sealed record DeviceEvent(DateTime Timestamp, DeviceEventId Event, byte Param1, ushort Flags) : DataBufRecord(Timestamp);

public sealed record FirmwareVersion(int BootMajor, int BootMinor, string BootDate, int TargetMajor, int TargetMinor, string TargetDate)
{
    public override string ToString() => $"{TargetMajor}.{TargetMinor} ({TargetDate}), boot {BootMajor}.{BootMinor}";
}

/// <summary>Energy spectrum with quadratic channel→keV calibration.</summary>
public sealed record Spectrum(TimeSpan Duration, float A0, float A1, float A2, int[] Counts)
{
    public double EnergyKeV(double channel) => A0 + A1 * channel + A2 * channel * channel;

    public long TotalCounts => Counts.Sum(c => (long)c);

    public double AverageCountRate => Duration.TotalSeconds > 0 ? TotalCounts / Duration.TotalSeconds : 0;

    /// <summary>Approximate channel for an energy (inverse of the calibration polynomial).</summary>
    public double ChannelOf(double keV)
    {
        if (Math.Abs(A2) < 1e-12) return (keV - A0) / A1;
        double disc = A1 * A1 - 4 * A2 * (A0 - keV);
        return (-A1 + Math.Sqrt(Math.Max(disc, 0))) / (2 * A2);
    }
}
