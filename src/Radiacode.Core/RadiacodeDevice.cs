using System.Buffers.Binary;
using System.Text;

namespace Radiacode.Core;

/// <summary>High-level RadiaCode-10x/110 client, independent of the transport (USB or BLE).</summary>
public sealed class RadiacodeDevice : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _seq;

    public DateTime BaseTime { get; private set; }
    public int SpectrumFormatVersion { get; private set; }
    public FirmwareVersion? Firmware { get; private set; }
    public string Transport => _transport.Description;

    private RadiacodeDevice(ITransport transport) => _transport = transport;

    /// <summary>Opens a session: exchange handshake, clock sync, firmware check, spectrum format.</summary>
    public static async Task<RadiacodeDevice> ConnectAsync(ITransport transport, CancellationToken ct = default)
    {
        var dev = new RadiacodeDevice(transport);
        try
        {
            await dev.ExecuteAsync(Command.SetExchange, [0x01, 0xFF, 0x12, 0xFF], ct);
            await dev.SetLocalTimeAsync(DateTime.Now, ct);
            await dev.WriteRequestAsync(Vsfr.DeviceTime, U32(0), ct);
            dev.BaseTime = DateTime.Now.AddSeconds(128);

            dev.Firmware = await dev.GetFirmwareVersionAsync(ct);
            if (dev.Firmware.TargetMajor < 4 || (dev.Firmware.TargetMajor == 4 && dev.Firmware.TargetMinor < 8))
                throw new ProtocolException($"Firmware {dev.Firmware.TargetMajor}.{dev.Firmware.TargetMinor} is too old, 4.8+ required");

            foreach (var line in (await dev.GetConfigurationAsync(ct)).Split('\n'))
            {
                if (line.StartsWith("SpecFormatVersion", StringComparison.Ordinal) &&
                    int.TryParse(line.Split('=')[1].Trim(), out var v))
                {
                    dev.SpectrumFormatVersion = v;
                    break;
                }
            }
            return dev;
        }
        catch
        {
            await dev.DisposeAsync();
            throw;
        }
    }

    public async Task<ByteReader> ExecuteAsync(Command cmd, byte[]? args = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            byte seqNo = (byte)(0x80 + _seq);
            _seq = (_seq + 1) % 32;
            args ??= [];
            var request = new byte[4 + 4 + args.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(request, (uint)(4 + args.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4), (ushort)cmd);
            request[6] = 0;
            request[7] = seqNo;
            args.CopyTo(request, 8);

            var response = await _transport.ExecuteAsync(request, ct);
            if (response.Length < 4 || !response.AsSpan(0, 4).SequenceEqual(request.AsSpan(4, 4)))
                throw new ProtocolException($"Response header mismatch for {cmd}");
            return new ByteReader(response, 4, response.Length - 4);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ByteReader> ReadRequestAsync(VirtualString id, CancellationToken ct = default)
    {
        var r = await ExecuteAsync(Command.RdVirtString, U32((uint)id), ct);
        uint retcode = r.ReadUInt32(), flen = r.ReadUInt32();
        if (retcode != 1) throw new ProtocolException($"{id}: retcode {retcode}");
        if (r.Remaining == flen + 1 && r.LastByte == 0) r.TrimLastByte(); // firmware quirk
        if (r.Remaining != flen) throw new ProtocolException($"{id}: size {r.Remaining}, expected {flen}");
        return r;
    }

    public async Task WriteRequestAsync(Vsfr id, byte[]? data = null, CancellationToken ct = default)
    {
        var payload = U32((uint)id).Concat(data ?? []).ToArray();
        var r = await ExecuteAsync(Command.WrVirtSfr, payload, ct);
        if (r.ReadUInt32() != 1) throw new ProtocolException($"Write {id} failed");
    }

    public Task SetLocalTimeAsync(DateTime t, CancellationToken ct = default) =>
        ExecuteAsync(Command.SetTime,
            [(byte)t.Day, (byte)t.Month, (byte)(t.Year - 2000), 0, (byte)t.Second, (byte)t.Minute, (byte)t.Hour, 0], ct);

    public async Task<FirmwareVersion> GetFirmwareVersionAsync(CancellationToken ct = default)
    {
        var r = await ExecuteAsync(Command.GetVersion, null, ct);
        ushort bMin = r.ReadUInt16(), bMaj = r.ReadUInt16();
        string bDate = r.ReadString();
        ushort tMin = r.ReadUInt16(), tMaj = r.ReadUInt16();
        string tDate = r.ReadString().TrimEnd('\0');
        return new FirmwareVersion(bMaj, bMin, bDate, tMaj, tMin, tDate);
    }

    public async Task<string> GetSerialNumberAsync(CancellationToken ct = default) =>
        Encoding.ASCII.GetString((await ReadRequestAsync(VirtualString.SerialNumber, ct)).ReadToEnd());

    /// <summary>Device configuration text (cp1251 on the device; the keys we need are ASCII).</summary>
    public async Task<string> GetConfigurationAsync(CancellationToken ct = default) =>
        Encoding.Latin1.GetString((await ReadRequestAsync(VirtualString.Configuration, ct)).ReadToEnd());

    /// <summary>Records accumulated since the previous call (real-time values, rare data, events).</summary>
    public async Task<IReadOnlyList<DataBufRecord>> ReadDataBufAsync(CancellationToken ct = default) =>
        Decoders.DecodeDataBuf(await ReadRequestAsync(VirtualString.DataBuf, ct), BaseTime);

    /// <summary>Current spectrum (since last spectrum reset).</summary>
    public async Task<Spectrum> ReadSpectrumAsync(CancellationToken ct = default) =>
        Decoders.DecodeSpectrum(await ReadRequestAsync(VirtualString.Spectrum, ct), SpectrumFormatVersion);

    /// <summary>Long-term accumulated spectrum.</summary>
    public async Task<Spectrum> ReadSpectrumAccumAsync(CancellationToken ct = default) =>
        Decoders.DecodeSpectrum(await ReadRequestAsync(VirtualString.SpectrumAccum, ct), SpectrumFormatVersion);

    public async Task<(float A0, float A1, float A2)> ReadEnergyCalibrationAsync(CancellationToken ct = default)
    {
        var r = await ReadRequestAsync(VirtualString.EnergyCalib, ct);
        return (r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    public async Task ResetSpectrumAsync(CancellationToken ct = default)
    {
        var args = U32((uint)VirtualString.Spectrum).Concat(U32(0)).ToArray();
        var r = await ExecuteAsync(Command.WrVirtString, args, ct);
        if (r.ReadUInt32() != 1) throw new ProtocolException("Spectrum reset failed");
    }

    /// <summary>Reads a 32-bit virtual SFR; null if this firmware does not expose it (e.g. DS/DR/CPS on 4.15).</summary>
    public async Task<uint?> ReadSfrAsync(Vsfr id, CancellationToken ct = default)
    {
        var r = await ExecuteAsync(Command.RdVirtSfr, U32((uint)id), ct);
        if (r.Remaining < 8 || r.ReadUInt32() != 1) return null;
        return r.ReadUInt32();
    }

    /// <summary>Detector temperature, °C.</summary>
    public async Task<float?> ReadTemperatureAsync(CancellationToken ct = default) =>
        await ReadSfrAsync(Vsfr.TempDegC, ct) is { } v ? BitConverter.UInt32BitsToSingle(v) : null;

    /// <summary>Raw charger-state register != 0. Observed as 1 both on and off USB power (FW 4.15), so its meaning is not established.</summary>
    public async Task<bool?> ReadChargingAsync(CancellationToken ct = default) =>
        await ReadSfrAsync(Vsfr.ChargerState, ct) is { } v ? v != 0 : null;

    public Task ResetDoseAsync(CancellationToken ct = default) => WriteRequestAsync(Vsfr.DoseReset, null, ct);

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync();
        _lock.Dispose();
    }
}
