using System.Buffers.Binary;
using Radiacode.Core;

namespace Radiacode.Core.Tests;

public class DecoderTests
{
    private static byte[] F32(float v) { var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, v); return b; }
    private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }
    private static byte[] I32(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); return b; }
    private static byte[] U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); return b; }
    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static readonly byte[] SpectrumHeader = Cat(U32(3600), F32(8.34f), F32(2.37f), F32(0.00033f));

    [Fact]
    public void SpectrumV0_ReadsRawUInt32Counts()
    {
        var data = Cat(SpectrumHeader, U32(5), U32(0), U32(123456));
        var sp = Decoders.DecodeSpectrum(new ByteReader(data), 0);
        Assert.Equal(TimeSpan.FromHours(1), sp.Duration);
        Assert.Equal(new[] { 5, 0, 123456 }, sp.Counts);
        Assert.Equal(8.34 + 2.37 * 10 + 0.00033 * 100, sp.EnergyKeV(10), 3);
    }

    [Fact]
    public void SpectrumV1_DecodesRunLengthAndDeltas()
    {
        // 2 zeros (vlen 0); 1 byte value 10 (vlen 1); deltas +5, -3 (vlen 2); +300 (vlen 3); +70000 (vlen 4); -1 (vlen 5)
        var data = Cat(SpectrumHeader,
            U16((2 << 4) | 0),
            U16((1 << 4) | 1), [10],
            U16((2 << 4) | 2), [5, unchecked((byte)-3)],
            U16((1 << 4) | 3), U16(300),
            U16((1 << 4) | 4), [0x70, 0x11, 0x01], // 0x011170 = 70000
            U16((1 << 4) | 5), I32(-1));
        var sp = Decoders.DecodeSpectrum(new ByteReader(data), 1);
        Assert.Equal(new[] { 0, 0, 10, 15, 12, 312, 70312, 70311 }, sp.Counts);
    }

    [Fact]
    public void DataBuf_RealRc110Backlog_DecodesStoredBatchesAndShortUserData()
    {
        // DATA_BUF payload captured from an RC-110 (FW 4.15) after 15 minutes unplugged: a single dose-rate
        // record, an event, rare data, a stored batch of 6 dose-rate samples, a 6-byte gid 4 record,
        // a batch of 3 more samples and trailing rare data / event records.
        var hex =
        "0A00027D76FEFF0204000000002B412C65D936C50000500B0007479CFEFF080040100C00035EA9FEFFBA5F0800DB8992" +
        "3AF510402640100D0102FD9BFEFF06008025000024040000ABAA30414AE7D636D0000010A404000000004641E414FD36" +
        "B4000010920400000000434165850537AC000010E4040000ABAA504147281337AE000010A5040000AB2A4641262EBA36" +
        "B6000010A40400000000464123DBF936B30000100E00046D94FFFF740F000000100F0102FD7CFFFF030080250000BB04" +
        "000055D54941D8810337B800001084040000ABAA4041D42BE536C3000010BE04000055554A41A60AE636B70000101000" +
        "037EF1FFFF02630800F7CC923AEB10062640101100078DFFFFFF07004050120003EEFFFFFF27630800F7CC923AEA10AC" +
        "264040";
        var baseTime = new DateTime(2026, 11, 13, 17, 35, 2);
        var recs = Decoders.DecodeDataBuf(new ByteReader(Convert.FromHexString(hex)), baseTime);

        var db = recs.OfType<DoseRateDb>().ToList();
        Assert.Equal(10, db.Count); // 1 single + 6 + 3 from the batches
        Assert.Equal(baseTime.AddMilliseconds(-911_390), db[1].Timestamp);
        for (int i = 2; i < db.Count; i++)
            Assert.Equal(96.0, (db[i].Timestamp - db[i - 1].Timestamp).TotalSeconds, 1);
        Assert.All(db, d => Assert.InRange(d.CountRate, 9f, 14f));
        Assert.Equal(1060u, db[1].Count);
        Assert.Equal(20.8f, db[1].DoseRateErrorPercent, 3);
        // decoding continues past the short gid 4 record up to the end of the buffer
        Assert.IsType<RareData>(recs.OfType<RareData>().Last());
        Assert.True(recs.OfType<DeviceEvent>().Count() >= 2);
    }

    [Fact]
    public void DataBuf_DecodesRealTimeRareAndEvent()
    {
        var baseTime = new DateTime(2026, 11, 10, 12, 0, 0);
        var data = Cat(
            [0, 0, 0], I32(100), F32(14.5f), F32(9.2e-6f), U16(49), U16(156), U16(64), [0],
            [1, 0, 3], I32(200), U32(3600), F32(5.9e-4f), U16(2000 + 284), U16(9869), U16(0),
            [2, 0, 7], I32(300), [(byte)DeviceEventId.PowerOn, 0], U16(0));
        var recs = Decoders.DecodeDataBuf(new ByteReader(data), baseTime);

        Assert.Equal(3, recs.Count);
        var rt = Assert.IsType<RealTimeData>(recs[0]);
        Assert.Equal(baseTime.AddSeconds(1), rt.Timestamp);
        Assert.Equal(92.0, rt.DoseRateNanoSvH, 1);
        Assert.Equal(15.6f, rt.DoseRateErrorPercent, 3);
        Assert.Equal(0.6345, rt.Hardness!.Value, 3);

        var rare = Assert.IsType<RareData>(recs[1]);
        Assert.Equal(2.84f, rare.TemperatureC, 3);
        Assert.Equal(98.69f, rare.ChargePercent, 3);
        Assert.Equal(5.9, rare.DoseMicroSv, 2);

        Assert.Equal(DeviceEventId.PowerOn, Assert.IsType<DeviceEvent>(recs[2]).Event);
    }

    [Fact]
    public void DataBuf_StopsOnSequenceJump()
    {
        var data = Cat([0, 0, 7], I32(0), [1, 0], U16(0), [5, 0, 7], I32(0), [1, 0], U16(0));
        Assert.Single(Decoders.DecodeDataBuf(new ByteReader(data), DateTime.Now));
    }

    [Fact]
    public void Spectrum_ChannelOfInvertsCalibration()
    {
        var sp = new Spectrum(TimeSpan.Zero, 8.34f, 2.37f, 0.00033f, new int[1024]);
        Assert.Equal(500.0, sp.ChannelOf(sp.EnergyKeV(500)), 3);
    }
}
