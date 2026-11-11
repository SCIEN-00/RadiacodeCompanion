namespace Radiacode.Core;

/// <summary>Command identifiers (from the MIT-licensed cdump/radiacode library).</summary>
public enum Command : ushort
{
    GetStatus = 0x0005,
    SetExchange = 0x0007,
    GetVersion = 0x000A,
    GetSerial = 0x000B,
    FwImageGetInfo = 0x0012,
    FwSignature = 0x0101,
    RdHwConfig = 0x0807,
    RdVirtSfr = 0x0824,
    WrVirtSfr = 0x0825,
    RdVirtString = 0x0826,
    WrVirtString = 0x0827,
    RdVirtSfrBatch = 0x082A,
    WrVirtSfrBatch = 0x082B,
    RdFlash = 0x081C,
    SetTime = 0x0A04,
}

/// <summary>Virtual strings.</summary>
public enum VirtualString : uint
{
    Configuration = 0x02,
    FwDescriptor = 0x03,
    SerialNumber = 0x08,
    TextMessage = 0x0F,
    MemSnapshot = 0xE0,
    DataBuf = 0x100,
    SfrFile = 0x101,
    Spectrum = 0x200,
    EnergyCalib = 0x202,
    SpectrumAccum = 0x205,
}

/// <summary>Virtual special-function registers (subset).</summary>
public enum Vsfr : uint
{
    DeviceCtrl = 0x0500,
    DeviceLang = 0x0502,
    DeviceOn = 0x0503,
    DeviceTime = 0x0504,
    DisplayBrightness = 0x0511,
    DisplayOffTime = 0x0513,
    DisplayOn = 0x0514,
    DisplayDirection = 0x0515,
    DisplayBacklightOn = 0x0516,
    SoundCtrl = 0x0520,
    SoundOn = 0x0522,
    VibroCtrl = 0x0530,
    VibroOn = 0x0531,
    DoseReset = 0x8007,
    TempDegC = 0x8024,
    ChargerState = 0xC200,
}

public enum DeviceEventId : byte
{
    PowerOff = 0,
    PowerOn = 1,
    LowBatteryShutdown = 2,
    ChangeDeviceParams = 3,
    DoseReset = 4,
    UserEvent = 5,
    BatteryEmptyAlarm = 6,
    ChargeStart = 7,
    ChargeStop = 8,
    DoseRateAlarm1 = 9,
    DoseRateAlarm2 = 10,
    DoseRateOffscale = 11,
    DoseAlarm1 = 12,
    DoseAlarm2 = 13,
    DoseOffscale = 14,
    TemperatureTooLow = 15,
    TemperatureTooHigh = 16,
    TextMessage = 17,
    MemorySnapshot = 18,
    SpectrumReset = 19,
    CountRateAlarm1 = 20,
    CountRateAlarm2 = 21,
    CountRateOffscale = 22,
}
