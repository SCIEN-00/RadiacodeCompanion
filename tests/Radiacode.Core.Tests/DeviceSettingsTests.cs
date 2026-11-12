using Radiacode.Core;

namespace Radiacode.Core.Tests;

public class DeviceSettingsTests
{
    [Fact]
    public void RawValuesFromRc110_RoundTrip()
    {
        var method = typeof(DeviceSettings).GetMethod("FromRaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        // Values read from an RC-110 on firmware 4.15 (2026-11-11).
        var raw = new Dictionary<Vsfr, uint>
        {
            [Vsfr.SoundOn] = 0, [Vsfr.SoundCtrl] = 8187, [Vsfr.VibroOn] = 0, [Vsfr.VibroCtrl] = 7423,
            [Vsfr.DisplayBrightness] = 0, [Vsfr.DisplayOffTime] = 1, [Vsfr.DisplayDirection] = 0,
            [Vsfr.DoseRateLevel1] = 40, [Vsfr.DoseRateLevel2] = 120,
            [Vsfr.CountRateLevel1] = 200, [Vsfr.CountRateLevel2] = 600,
            [Vsfr.DoseLevel1] = 999999999, [Vsfr.DoseLevel2] = 999999999,
        };
        var s = (DeviceSettings)method.Invoke(null, [raw])!;
        Assert.Equal(400, s.DoseRateAlarm1NSvH);
        Assert.Equal(1200, s.DoseRateAlarm2NSvH);
        Assert.Equal(20, s.CountRateAlarm1Cps);
        Assert.Equal(10u, s.DisplayOffSeconds);
        Assert.True(s.ClickSound);

        var toRaw = typeof(DeviceSettings).GetMethod("ToRaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, [])!;
        var back = (IReadOnlyDictionary<Vsfr, uint>)toRaw.Invoke(s, null)!;
        foreach (var (k, v) in raw)
            Assert.Equal(k == Vsfr.VibroCtrl ? v & ~(uint)NotifyEvents.Clicks : v, back[k]);
    }

    [Fact]
    public void OptionalRegisters_OnlyWrittenWhenReported()
    {
        var fromRaw = typeof(DeviceSettings).GetMethod("FromRaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var toRaw = typeof(DeviceSettings).GetMethod("ToRaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, [])!;
        var baseRaw = new Dictionary<Vsfr, uint>
        {
            [Vsfr.SoundOn] = 0, [Vsfr.SoundCtrl] = 8185, [Vsfr.VibroOn] = 0, [Vsfr.VibroCtrl] = 7423,
            [Vsfr.DisplayBrightness] = 9, [Vsfr.DisplayOffTime] = 5, [Vsfr.DisplayDirection] = 0,
            [Vsfr.DoseRateLevel1] = 40, [Vsfr.DoseRateLevel2] = 120, [Vsfr.CountRateLevel1] = 200, [Vsfr.CountRateLevel2] = 600,
            [Vsfr.DoseLevel1] = 999999999, [Vsfr.DoseLevel2] = 999999999,
        };
        var old = (DeviceSettings)fromRaw.Invoke(null, [baseRaw])!;
        Assert.Equal(300u, old.DisplayOffSeconds); // index 5 = 5 min
        Assert.DoesNotContain(Vsfr.SoundVolume, ((IReadOnlyDictionary<Vsfr, uint>)toRaw.Invoke(old, null)!).Keys);

        // Values read from an RC-110 on firmware 4.15 (2026-11-12).
        var full = new Dictionary<Vsfr, uint>(baseRaw)
        {
            [Vsfr.SoundVolume] = 50, [Vsfr.DeviceLang] = 0, [Vsfr.DoseUnits] = 1, [Vsfr.UseNanoSvH] = 1,
            [Vsfr.CountRateUnits] = 0, [Vsfr.TempUnits] = 0,
        };
        var s = (DeviceSettings)fromRaw.Invoke(null, [full])!;
        Assert.Equal(50u, s.SoundVolume);
        Assert.True(s.DoseInSievert);
        Assert.False(s.CountsPerMinute);
        Assert.True(s.English); // device menu was in English with DEVICE_LANG = 0

        // Optional registers must actually be read (regression: the list was once emptied by mistake).
        var optional = (Vsfr[])typeof(DeviceSettings).GetField("OptionalRegisters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        Assert.Contains(Vsfr.SoundVolume, optional);
        Assert.Contains(Vsfr.DeviceCtrl, optional);
        Assert.DoesNotContain(Vsfr.DeviceLang, optional);

        // Mapped with the device menu (Settings → Signals), RC-110 FW 4.15, 2026-11-12.
        var sig = s with { DeviceCtrl = 0x2B, SoundCtrl = 0x1DF8, VibroCtrl = 0x1CFF };
        Assert.True(sig.Led);                 // Light on: DEVICE_CTRL 0x23 → 0x2B
        Assert.True(sig.ConnectionSound);     // Connection on: bit 8
        Assert.False(sig.PowerSound);         // Power off: bit 9 cleared
        Assert.False(sig.ButtonSound);
        Assert.True(sig.ButtonVibration);
        var back = (IReadOnlyDictionary<Vsfr, uint>)toRaw.Invoke(s with { SoundVolume = 250, Fahrenheit = true }, null)!;
        Assert.Equal(100u, back[Vsfr.SoundVolume]);
        Assert.Equal(1u, back[Vsfr.TempUnits]);
        Assert.Equal(0u, back[Vsfr.DeviceLang]); // kept as read, never changed by the app
    }

    [Fact]
    public void WithFlag_KeepsUnknownBits()
    {
        uint ctrl = 8187; // 0x1FFB: includes bits beyond the known ones
        uint off = DeviceSettings.WithFlag(ctrl, NotifyEvents.Clicks, false);
        Assert.Equal(ctrl & ~2u, off);
        Assert.Equal(ctrl, DeviceSettings.WithFlag(off, NotifyEvents.Clicks, true));
    }
}
