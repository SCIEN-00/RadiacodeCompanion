namespace Radiacode.Core;

/// <summary>Decoders for the VS_DATA_BUF and spectrum payloads (ported from cdump/radiacode, MIT).</summary>
public static class Decoders
{
    public static IReadOnlyList<DataBufRecord> DecodeDataBuf(ByteReader r, DateTime baseTime)
    {
        var result = new List<DataBufRecord>();
        int? nextSeq = null;
        while (r.Remaining >= 7)
        {
            byte seq = r.ReadByte();
            byte eid = r.ReadByte();
            byte gid = r.ReadByte();
            int tsOffset = r.ReadInt32();
            var ts = baseTime.AddMilliseconds(tsOffset * 10.0);

            if (nextSeq is not null && nextSeq != seq)
                break; // sequence jump: rest of buffer is unreliable
            nextSeq = (seq + 1) % 256;

            switch (eid, gid)
            {
                case (0, 0):
                {
                    float cr = r.ReadSingle(), dr = r.ReadSingle();
                    ushort crErr = r.ReadUInt16(), drErr = r.ReadUInt16(), flags = r.ReadUInt16();
                    byte rtFlags = r.ReadByte();
                    result.Add(new RealTimeData(ts, cr, crErr / 10f, dr, drErr / 10f, flags, rtFlags));
                    // Newer firmware (seen on 4.15) appends one extra byte to real-time records.
                    // Detect it via the next record's sequence number so older 15-byte layouts still work.
                    if (r.Remaining == 1 || (r.Remaining >= 8 && r.PeekByte() != nextSeq && r.PeekByte(1) == nextSeq))
                        r.Skip(1);
                    break;
                }
                case (0, 1):
                    result.Add(new RawData(ts, r.ReadSingle(), r.ReadSingle()));
                    break;
                case (0, 2):
                {
                    uint count = r.ReadUInt32();
                    float cr = r.ReadSingle(), dr = r.ReadSingle();
                    ushort drErr = r.ReadUInt16(), flags = r.ReadUInt16();
                    result.Add(new DoseRateDb(ts, count, cr, dr, drErr / 10f, flags));
                    break;
                }
                case (0, 3):
                {
                    uint duration = r.ReadUInt32();
                    float dose = r.ReadSingle();
                    ushort temp = r.ReadUInt16(), charge = r.ReadUInt16(), flags = r.ReadUInt16();
                    result.Add(new RareData(ts, duration, dose, (temp - 2000) / 100f, charge / 100f, flags));
                    break;
                }
                case (0, 4): // UserData
                case (0, 5): // ScheduleData
                {
                    // FW 4.15 sends 6-byte records here, older firmware 16 bytes; pick the length the next sequence number confirms.
                    int len = FixedLength(r, nextSeq.Value, 6, 16);
                    if (len == 0) return result;
                    r.Skip(len);
                    break;
                }
                case (0, 6): // AccelData
                    r.Skip(6);
                    break;
                case (0, 7):
                {
                    byte ev = r.ReadByte(), p1 = r.ReadByte();
                    ushort flags = r.ReadUInt16();
                    result.Add(new DeviceEvent(ts, (DeviceEventId)ev, p1, flags));
                    break;
                }
                case (0, 8): // RawCountRate
                case (0, 9): // RawDoseRate
                    r.Skip(6);
                    break;
                case (1, 2):
                {
                    // Stored history: a batch of dose-rate samples, the first at ts, then one every interval (10 ms units).
                    ushort samples = r.ReadUInt16();
                    uint interval = r.ReadUInt32();
                    if (r.Remaining < samples * 16) return result;
                    for (int i = 0; i < samples; i++)
                    {
                        uint count = r.ReadUInt32();
                        float cr = r.ReadSingle(), dr = r.ReadSingle();
                        ushort drErr = r.ReadUInt16(), flags = r.ReadUInt16();
                        result.Add(new DoseRateDb(ts.AddMilliseconds(i * (double)interval * 10.0), count, cr, dr, drErr / 10f, flags));
                    }
                    break;
                }
                case (1, 1):
                case (1, 3):
                {
                    ushort samples = r.ReadUInt16();
                    r.Skip(4); // sample interval
                    int size = gid == 1 ? 8 : 14;
                    if (r.Remaining < size * samples) return result;
                    r.Skip(size * samples);
                    break;
                }
                default:
                    return result; // unknown record: cannot know its length
            }
        }
        return result;
    }

    /// <summary>Returns whichever candidate length is followed by the expected sequence number (or ends the buffer), else 0.</summary>
    private static int FixedLength(ByteReader r, int nextSeq, params int[] candidates)
    {
        foreach (int len in candidates)
            if (r.Remaining == len || (r.Remaining >= len + 7 && r.PeekByte(len) == nextSeq)) return len;
        return 0;
    }

    public static Spectrum DecodeSpectrum(ByteReader r, int formatVersion)
    {
        uint seconds = r.ReadUInt32();
        float a0 = r.ReadSingle(), a1 = r.ReadSingle(), a2 = r.ReadSingle();
        int[] counts = formatVersion switch
        {
            0 => DecodeCountsV0(r),
            1 => DecodeCountsV1(r),
            _ => throw new ProtocolException($"Unsupported spectrum format version {formatVersion}"),
        };
        return new Spectrum(TimeSpan.FromSeconds(seconds), a0, a1, a2, counts);
    }

    private static int[] DecodeCountsV0(ByteReader r)
    {
        var list = new List<int>(1024);
        while (r.Remaining >= 4) list.Add(unchecked((int)r.ReadUInt32()));
        return list.ToArray();
    }

    /// <summary>Run-length + delta encoding used by firmware with SpecFormatVersion=1.</summary>
    private static int[] DecodeCountsV1(ByteReader r)
    {
        var list = new List<int>(1024);
        int last = 0;
        while (r.Remaining >= 2)
        {
            ushort u16 = r.ReadUInt16();
            int cnt = (u16 >> 4) & 0x0FFF;
            int vlen = u16 & 0x0F;
            for (int i = 0; i < cnt; i++)
            {
                int v = vlen switch
                {
                    0 => 0,
                    1 => r.ReadByte(),
                    2 => last + r.ReadSByte(),
                    3 => last + r.ReadInt16(),
                    4 => last + Read24(r),
                    5 => last + r.ReadInt32(),
                    _ => throw new ProtocolException($"Unsupported vlen={vlen} in spectrum v1"),
                };
                last = v;
                list.Add(v);
            }
        }
        return list.ToArray();
    }

    private static int Read24(ByteReader r)
    {
        byte a = r.ReadByte(), b = r.ReadByte();
        sbyte c = r.ReadSByte();
        return (c << 16) | (b << 8) | a;
    }
}
