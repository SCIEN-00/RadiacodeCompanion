using Radiacode.Core;
using Radiacode.Transport.Ble;
using Radiacode.Transport.LibUsb;

// Usage: rccli [--ble] [info|live [seconds]|spectrum [file.csv]]
bool ble = args.Contains("--ble");
args = args.Where(a => a != "--ble").ToArray();
var mode = args.Length > 0 ? args[0] : "info";
if (args.Length > 0 && args[0] == "list")
{
    foreach (var u in LibUsbTransport.List()) Console.WriteLine($"USB  key={u.Key}  serial={u.UsbSerial ?? "(none)"}");
    if (ble)
        foreach (var b in await BleTransport.ScanAsync(TimeSpan.FromSeconds(8))) Console.WriteLine($"BLE  id={b.Id}  name={b.Name}");
    return;
}

if (args.Length > 1 && args[0] == "gaincheck")
{
    // Offline: run the K-40 gain check on a spectrum CSV written by "rccli spectrum file.csv".
    var rows = File.ReadLines(args[1]).Skip(1).Select(l => l.Split(';'))
        .Select(p => (Ch: int.Parse(p[0]), E: double.Parse(p[1].Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture), N: int.Parse(p[2])))
        .ToArray();
    // Recover E = a0 + a1·ch + a2·ch² from three points.
    var (c0, c1, c2) = (rows[0], rows[rows.Length / 2], rows[^1]);
    double d1 = (c1.E - c0.E) / (c1.Ch - c0.Ch), d2 = (c2.E - c1.E) / (c2.Ch - c1.Ch);
    double a2 = (d2 - d1) / (c2.Ch - c0.Ch), a1 = d1 - a2 * (c0.Ch + c1.Ch), a0 = c0.E - a1 * c0.Ch - a2 * c0.Ch * c0.Ch;
    var spec = new Spectrum(TimeSpan.Zero, (float)a0, (float)a1, (float)a2, rows.Select(r => r.N).ToArray());
    var fix = GainCorrection.FindK40(spec);
    Console.WriteLine(fix is null ? "No clear K-40 peak." : $"K-40 found at {fix.FoundKeV:F1} keV ({fix.Significance:F1} σ) → gain ×{fix.Factor:F4}");
    if (fix is not null)
        foreach (var r in SpectrumAnalysis.AnalyzeAll(fix.Apply(spec)).Where(r => r.Significance >= 1.5))
            Console.WriteLine($"  {r.Line.Nuclide,-7} {r.Line.EnergyKeV,7:F1} keV  {r.Significance,5:F1} σ");
    return;
}
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

ITransport transport = ble ? await BleTransport.ConnectAsync(TimeSpan.FromSeconds(20), cts.Token) : LibUsbTransport.Open();
await using var dev = await RadiacodeDevice.ConnectAsync(transport, cts.Token);
Console.WriteLine($"Serial:   {await dev.GetSerialNumberAsync(cts.Token)}");
Console.WriteLine($"Firmware: {dev.Firmware}");
Console.WriteLine($"Spectrum format v{dev.SpectrumFormatVersion}, transport {dev.Transport}");

switch (mode)
{
    case "cmd":
    {
        // Debug: send a raw command (hex code) with raw argument bytes (hex), print the raw answer.
        // e.g. rccli --ble cmd 081C 0000000010000000
        var code = (Command)Convert.ToUInt16(args[1], 16);
        var argBytes = args.Length > 2 ? Convert.FromHexString(args[2]) : [];
        try
        {
            var rb = await dev.ExecuteAsync(code, argBytes, cts.Token);
            var bytes = rb.ReadToEnd();
            Console.WriteLine($"{bytes.Length} bytes: {Convert.ToHexString(bytes[..Math.Min(bytes.Length, 96)])}");
        }
        catch (Exception ex) { Console.WriteLine($"ERROR {ex.GetType().Name}: {ex.Message}"); }
        break;
    }
    case "regs":
    {
        // Debug: read every register listed in the device's own register map (SFR_FILE) and print its value.
        var map = System.Text.Encoding.Latin1.GetString((await dev.ReadRequestAsync((VirtualString)0x101, cts.Token)).ReadToEnd());
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(map, @"\[([^\]]+)\]\s*\r?\nAddr=0x([0-9A-Fa-f]+)((?:\r?\n(?!\[)[^\r\n]*)*)"))
        {
            var name = m.Groups[1].Value; var addr = Convert.ToUInt32(m.Groups[2].Value, 16); var meta = m.Groups[3].Value;
            uint? v = null;
            try { v = await dev.ReadSfrAsync((Vsfr)addr, cts.Token); } catch (Exception ex) { Console.WriteLine($"{name,-28} 0x{addr:X4}  ERROR {ex.Message}"); continue; }
            string shown = v is not { } raw ? "n/a"
                : meta.Contains("Type=float") ? BitConverter.UInt32BitsToSingle(raw).ToString("G6", System.Globalization.CultureInfo.InvariantCulture)
                : meta.Contains("Signed=1") && meta.Contains("Size=2") ? ((short)raw).ToString()
                : meta.Contains("Signed=1") && meta.Contains("Size=1") ? ((sbyte)raw).ToString()
                : meta.Contains("Signed=1") ? ((int)raw).ToString() : $"{raw} (0x{raw:X})";
            Console.WriteLine($"{name,-28} 0x{addr:X4}  {shown}");
        }
        break;
    }
    case "probe":
    {
        // Debug: read the virtual strings whose content is not known (read-only, DATA_BUF excluded) and the
        // hardware config, and write each raw answer to <dir>/<name>.bin for offline decoding.
        var dir = args.Length > 1 ? args[1] : "probe";
        Directory.CreateDirectory(dir);
        var targets = new (string Name, Func<Task<byte[]>> Read)[]
        {
            ("vs_02_configuration", async () => (await dev.ReadRequestAsync((VirtualString)0x02, cts.Token)).ReadToEnd()),
            ("vs_03_fw_descriptor", async () => (await dev.ReadRequestAsync((VirtualString)0x03, cts.Token)).ReadToEnd()),
            ("vs_0F_text_message", async () => (await dev.ReadRequestAsync((VirtualString)0x0F, cts.Token)).ReadToEnd()),
            ("vs_E0_mem_snapshot", async () => (await dev.ReadRequestAsync((VirtualString)0xE0, cts.Token)).ReadToEnd()),
            ("vs_101_sfr_file", async () => (await dev.ReadRequestAsync((VirtualString)0x101, cts.Token)).ReadToEnd()),
            ("cmd_0807_hw_config", async () => (await dev.ExecuteAsync(Command.RdHwConfig, null, cts.Token)).ReadToEnd()),
        };
        foreach (var (name, read) in targets)
        {
            try
            {
                var bytes = await read();
                File.WriteAllBytes(Path.Combine(dir, name + ".bin"), bytes);
                Console.WriteLine($"{name}: {bytes.Length} bytes");
            }
            catch (Exception ex) { Console.WriteLine($"{name}: ERROR {ex.GetType().Name}: {ex.Message}"); }
        }
        break;
    }
    case "vs":
    {
        // Debug: read a virtual string by hex id, print as text + hex.
        var id = (VirtualString)Convert.ToUInt32(args[1], 16);
        var rb = await dev.ReadRequestAsync(id, cts.Token);
        var bytes = rb.ReadToEnd();
        Console.WriteLine(System.Text.Encoding.Latin1.GetString(bytes));
        Console.WriteLine(Convert.ToHexString(bytes));
        break;
    }
    case "flags":
    {
        // Debug: watch status flags and device events (e.g. to find the charging bit).
        int seconds = args.Length > 1 ? int.Parse(args[1]) : 60;
        var until = DateTime.Now.AddSeconds(seconds);
        string last = "";
        while (DateTime.Now < until && !cts.IsCancellationRequested)
        {
            foreach (var rec in await dev.ReadDataBufAsync(cts.Token))
            {
                string line = rec switch
                {
                    RealTimeData rt => $"rt flags=0x{rt.Flags:X4} rtflags=0x{rt.RealTimeFlags:X2}",
                    RareData rd => $"rare flags=0x{rd.Flags:X4} battery={rd.ChargePercent:F0}%",
                    DoseRateDb db => $"db flags=0x{db.Flags:X4}",
                    DeviceEvent ev => $"EVENT {ev.Event} p1={ev.Param1} flags=0x{ev.Flags:X4}",
                    _ => "",
                };
                if (line.Length > 0 && (line != last || rec is DeviceEvent)) Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
                if (rec is RealTimeData) last = line;
            }
            await Task.Delay(1000, cts.Token).ContinueWith(_ => { });
        }
        break;
    }
    case "trig":
    {
        // Debug: which command makes the device emit RareData? arg: time | localtime | none
        await dev.ReadDataBufAsync(cts.Token);
        await Task.Delay(2500, cts.Token);
        await dev.ReadDataBufAsync(cts.Token);
        switch (args.ElementAtOrDefault(1))
        {
            case "time": await dev.WriteRequestAsync(Vsfr.DeviceTime, BitConverter.GetBytes(0u), cts.Token); break;
            case "localtime": await dev.SetLocalTimeAsync(DateTime.Now, cts.Token); break;
            case "exchange": await dev.ExecuteAsync(Command.SetExchange, [0x01, 0xFF, 0x12, 0xFF], cts.Token); break;
        }
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(1000, cts.Token);
            var recs = await dev.ReadDataBufAsync(cts.Token);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {string.Join(", ", recs.Select(x => x.GetType().Name))}");
        }
        break;
    }
    case "sfrb":
    {
        // Debug: batch-read virtual SFRs (RD_VIRT_SFR_BATCH): u32 count + u32 ids -> u32 valid mask + u32 values.
        var ids = args.Skip(1).Select(a => Convert.ToUInt32(a, 16)).ToArray();
        var req = new List<byte>(BitConverter.GetBytes((uint)ids.Length));
        foreach (var id in ids) req.AddRange(BitConverter.GetBytes(id));
        var rb = await dev.ExecuteAsync(Command.RdVirtSfrBatch, req.ToArray(), cts.Token);
        var bytes = rb.ReadToEnd();
        Console.WriteLine(Convert.ToHexString(bytes));
        uint mask = BitConverter.ToUInt32(bytes, 0);
        for (int i = 0; i < ids.Length && 4 + 4 * i + 4 <= bytes.Length; i++)
            Console.WriteLine($"0x{ids[i]:X4} valid={(mask >> i) & 1} u32={BitConverter.ToUInt32(bytes, 4 + 4 * i)} f32={BitConverter.ToSingle(bytes, 4 + 4 * i):G6}");
        break;
    }
    case "sfr":
    {
        // Debug: read virtual SFRs by hex id(s); print raw u32 / i32 / float.
        foreach (var a in args.Skip(1))
        {
            uint id = Convert.ToUInt32(a, 16);
            var rb = await dev.ExecuteAsync(Command.RdVirtSfr, BitConverter.GetBytes(id), cts.Token);
            var bytes = rb.ReadToEnd();
            string val = bytes.Length >= 8 ? $"u32={BitConverter.ToUInt32(bytes, 4)} i32={BitConverter.ToInt32(bytes, 4)} f32={BitConverter.ToSingle(bytes, 4):G6}" : "";
            Console.WriteLine($"0x{id:X4}: {Convert.ToHexString(bytes)} {val}");
        }
        break;
    }
    case "rawbuf":
    {
        // Debug: hex-dump raw DATA_BUF payloads (no backlog drop) to inspect record types.
        int seconds = args.Length > 1 ? int.Parse(args[1]) : 30;
        var until = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < until && !cts.IsCancellationRequested)
        {
            var rb = await dev.ReadRequestAsync(VirtualString.DataBuf, cts.Token);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {rb.Remaining} {Convert.ToHexString(rb.RemainingSpan)}");
            await Task.Delay(1000, cts.Token).ContinueWith(_ => { });
        }
        break;
    }
    case "live":
    {
        int seconds = args.Length > 1 ? int.Parse(args[1]) : 30;
        var until = DateTime.Now.AddSeconds(seconds);
        foreach (var rec in (await dev.ReadDataBufAsync(cts.Token)).OfType<RareData>().TakeLast(1))
            Console.WriteLine($"{rec.Timestamp:HH:mm:ss}  dose {rec.DoseMicroSv:F2} µSv / {TimeSpan.FromSeconds(rec.DurationSeconds)}  {rec.TemperatureC:F1} °C  battery {rec.ChargePercent:F0} %  (backlog)");
        while (DateTime.Now < until && !cts.IsCancellationRequested)
        {
            await Task.Delay(1000, cts.Token).ContinueWith(_ => { });
            foreach (var rec in await dev.ReadDataBufAsync(cts.Token))
            {
                switch (rec)
                {
                    case RealTimeData rt:
                        Console.WriteLine($"{rt.Timestamp:HH:mm:ss}  {rt.DoseRateNanoSvH,7:F1} nSv/h ±{rt.DoseRateErrorPercent,4:F1}%  " +
                                          $"{rt.CountRate,6:F2} cps ±{rt.CountRateErrorPercent,4:F1}%  hardness {rt.Hardness:F2}");
                        break;
                    case RareData rd:
                        Console.WriteLine($"{rd.Timestamp:HH:mm:ss}  dose {rd.DoseMicroSv:F2} µSv / {TimeSpan.FromSeconds(rd.DurationSeconds)}  " +
                                          $"{rd.TemperatureC:F1} °C  battery {rd.ChargePercent:F0} %");
                        break;
                    case DeviceEvent ev:
                        Console.WriteLine($"{ev.Timestamp:HH:mm:ss}  event {ev.Event}");
                        break;
                }
            }
        }
        break;
    }
    case "spectrum":
    {
        var sp = await dev.ReadSpectrumAsync(cts.Token);
        Console.WriteLine($"Spectrum: {sp.Duration}, {sp.Counts.Length} ch, {sp.TotalCounts} counts, {sp.AverageCountRate:F2} cps avg");
        Console.WriteLine($"Calibration: E = {sp.A0:F3} + {sp.A1:F5}·ch + {sp.A2:E3}·ch²  → max {sp.EnergyKeV(sp.Counts.Length - 1):F0} keV");
        if (args.Length > 1)
        {
            await using var w = new StreamWriter(args[1]);
            await w.WriteLineAsync("channel;energy_keV;counts");
            for (int ch = 0; ch < sp.Counts.Length; ch++)
                await w.WriteLineAsync($"{ch};{sp.EnergyKeV(ch):F2};{sp.Counts[ch]}");
            Console.WriteLine($"Saved {args[1]}");
        }
        break;
    }
    default:
    {
        foreach (var rec in await dev.ReadDataBufAsync(cts.Token))
            Console.WriteLine(rec);
        break;
    }
}
