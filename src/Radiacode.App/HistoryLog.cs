using System.Globalization;
using System.Text;

namespace Radiacode.App;

/// <summary>
/// Persists every real-time reading to one CSV file per day (time, dose rate, count rate) so history survives restarts.
/// Writes are buffered and flushed every few seconds; files older than <see cref="KeepDays"/> are removed.
/// </summary>
public sealed class HistoryLog : IDisposable
{
    public const int KeepDays = 90;
    private const string Header = "time,dose_rate_nSv_h,count_rate_cps";
    private readonly StringBuilder _buffer = new();
    private DateTime _bufferDay;
    private DateTime _lastFlush = DateTime.Now;

    public static string Folder => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "history");

    private static string FileFor(DateTime day) => Path.Combine(Folder, $"{day:yyyy-MM-dd}.csv");

    public void Append(DateTime time, double doseNSvH, double cps)
    {
        if (_buffer.Length > 0 && time.Date != _bufferDay) Flush();
        _bufferDay = time.Date;
        _buffer.Append(time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
               .Append(doseNSvH.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
               .Append(cps.ToString("F3", CultureInfo.InvariantCulture)).Append('\n');
        if ((DateTime.Now - _lastFlush).TotalSeconds >= 10) Flush();
    }

    public void Flush()
    {
        _lastFlush = DateTime.Now;
        if (_buffer.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var path = FileFor(_bufferDay);
            bool isNew = !File.Exists(path);
            File.AppendAllText(path, (isNew ? Header + "\n" : "") + _buffer);
            _buffer.Clear();
        }
        catch
        {
            // Keep the buffer and try again on the next flush (e.g. file briefly locked).
            if (_buffer.Length > 1_000_000) _buffer.Clear();
        }
    }

    /// <summary>Readings at or after <paramref name="from"/>, oldest first.</summary>
    public static IEnumerable<(DateTime Time, double DoseNSvH, double Cps)> LoadSince(DateTime from)
    {
        for (var day = from.Date; day <= DateTime.Today; day = day.AddDays(1))
        {
            var path = FileFor(day);
            if (!File.Exists(path)) continue;
            IEnumerable<string> lines;
            try { lines = File.ReadAllLines(path); } catch { continue; }
            foreach (var line in lines)
            {
                var p = line.Split(',');
                if (p.Length < 3
                    || !DateTime.TryParseExact(p[0], "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                    || t < from
                    || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dose)
                    || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var cps))
                    continue;
                yield return (t, dose, cps);
            }
        }
    }

    /// <summary>
    /// Merges readings that were stored on the device while it was not connected into the daily files:
    /// skips timestamps that are already there (to the second) and keeps each file in time order.
    /// Returns the number of readings added.
    /// </summary>
    public static int Import(IEnumerable<(DateTime Time, double DoseNSvH, double Cps)> readings)
    {
        int added = 0;
        foreach (var day in readings.GroupBy(r => r.Time.Date))
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var path = FileFor(day.Key);
                var lines = new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path).Skip(1))
                        if (line.Length >= 19) lines[line[..19]] = line;
                foreach (var (time, dose, cps) in day)
                {
                    var key = time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                    if (lines.ContainsKey(key)) continue;
                    lines[key] = key + "," + dose.ToString("F2", CultureInfo.InvariantCulture) + ","
                                     + cps.ToString("F3", CultureInfo.InvariantCulture);
                    added++;
                }
                File.WriteAllText(path, Header + "\n" + string.Join("\n", lines.Values) + "\n");
            }
            catch
            {
                // A locked or unreadable file only loses the import for that day.
            }
        }
        return added;
    }
    public static void RemoveOld()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var file in Directory.GetFiles(Folder, "*.csv"))
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    && day < DateTime.Today.AddDays(-KeepDays))
                    File.Delete(file);
        }
        catch
        {
            // housekeeping only
        }
    }

    public void Dispose() => Flush();
}
