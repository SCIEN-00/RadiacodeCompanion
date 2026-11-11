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
    public void WithFlag_KeepsUnknownBits()
    {
        uint ctrl = 8187; // 0x1FFB: includes bits beyond the known ones
        uint off = DeviceSettings.WithFlag(ctrl, NotifyEvents.Clicks, false);
        Assert.Equal(ctrl & ~2u, off);
        Assert.Equal(ctrl, DeviceSettings.WithFlag(off, NotifyEvents.Clicks, true));
    }
}
