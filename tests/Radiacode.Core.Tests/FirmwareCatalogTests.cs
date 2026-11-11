using Radiacode.Core;

namespace Radiacode.Core.Tests;

public class FirmwareCatalogTests
{
    // Phrasing as it appears on radiacode.com/changelog (Nov 2026).
    private const string Html = """
        <div><h3>Device Firmware</h3><p>Added firmware version 4.15 for Radiacode 100 series devices, introducing temperature alarms.</p>
        <p>Added firmware version 4.17 for <b>Radiacode ZERO</b>, featuring improved measurement stability.</p>
        <p>Added firmware version 4.15 for Radiacode 102/103/103G, introducing temperature alarms.</p>
        <p>Added firmware version 4.14 for Radiacode 110.</p></div>
        """;

    [Theory]
    [InlineData("RC-110-001702", 4, 15)]
    [InlineData("RC-103-000123", 4, 15)]
    public void PicksNewestForModel_IgnoringZero(string serial, int major, int minor)
    {
        var model = FirmwareCatalog.ModelOf(serial);
        Assert.NotNull(model);
        Assert.Equal((major, minor), FirmwareCatalog.LatestFor(Html, model!));
    }

    [Fact]
    public void NoAnnouncement_ReturnsNull() =>
        Assert.Null(FirmwareCatalog.LatestFor("<p>nothing here</p>", "110"));
}
