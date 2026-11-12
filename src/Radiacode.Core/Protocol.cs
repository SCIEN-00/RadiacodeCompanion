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
    DoseRateLevel1 = 0x8000,   // µrem/h (older firmware: µR/h)
    DoseRateLevel2 = 0x8001,
    DoseUnits = 0x8004,
    DoseReset = 0x8007,
    CountRateLevel1 = 0x8008,  // counts per 10 s
    CountRateLevel2 = 0x8009,
    CountRateUnits = 0x8013,
    DoseLevel1 = 0x8014,       // µrem
    DoseLevel2 = 0x8015,
    TempDegC = 0x8024,
    ChargerState = 0xC200,

    // Found in the device's own register map (VS SFR_FILE 0x101), RC-110 firmware 4.15.
    DisplayContrast = 0x0512,
    SoundVolume = 0x0521,
    BleTxPower = 0x0700,
    AlarmMode = 0x05E0,
    CpsFilter = 0x8005,
    UseNanoSvH = 0x800C,
    TempUnits = 0x8016,
    RawTempDegC = 0x8033,
    TempUpDegC = 0x8034,
    TempDnDegC = 0x8035,
    BiasMilliVolt = 0xC000,
    ComparatorLevel = 0xC001,
    ChargerMonitorTempDegC = 0xC300,
    SysMcuId0 = 0xFFFF0000,
    SysMcuId1 = 0xFFFF0001,
    SysMcuId2 = 0xFFFF0002,
    SysDeviceId = 0xFFFF0005,
    SysRxSize = 0xFFFF0007,
    SysTxSize = 0xFFFF0008,
    SysMcuVref = 0xFFFF000C,
    SysMcuTemp = 0xFFFF000D,
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
