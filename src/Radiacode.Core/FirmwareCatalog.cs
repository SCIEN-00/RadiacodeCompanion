using System.Net;
using System.Text.RegularExpressions;

namespace Radiacode.Core;

/// <summary>
/// Finds the newest firmware RadiaCode has announced for a model, from the public changelog
/// (https://www.radiacode.com/changelog). RadiaCode has no public firmware API: firmware ships inside
/// their apps, and the changelog lines read like "Added firmware version 4.15 for Radiacode 100 series devices".
/// </summary>
public static partial class FirmwareCatalog
{
    public const string ChangelogUrl = "https://www.radiacode.com/changelog";
    public const string DownloadsUrl = "https://radiacode.com/downloads";

    [GeneratedRegex(@"firmware version (\d+)\.(\d+) for Radiacode ([^.;:]{1,60})", RegexOptions.IgnoreCase)]
    private static partial Regex Announcement();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    [GeneratedRegex(@"RC-(\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex SerialModel();

    /// <summary>Model number from a serial like "RC-110-001702" → "110".</summary>
    public static string? ModelOf(string serial) => SerialModel().Match(serial) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>Newest announced (major, minor) for the model, or null if the page mentions none.</summary>
    public static (int Major, int Minor)? LatestFor(string changelogHtml, string model)
    {
        var text = Space().Replace(WebUtility.HtmlDecode(Tag().Replace(changelogHtml, " ")), " ");
        (int, int)? best = null;
        foreach (Match m in Announcement().Matches(text))
        {
            var target = m.Groups[3].Value;
            if (target.Contains("ZERO", StringComparison.OrdinalIgnoreCase)) continue;
            bool applies = target.Contains(model, StringComparison.Ordinal)
                           || (model.StartsWith('1') && (target.Contains("100 series", StringComparison.OrdinalIgnoreCase)
                                                         || target.Contains("10x", StringComparison.OrdinalIgnoreCase)));
            if (!applies) continue;
            var v = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            if (best is null || v.CompareTo(best.Value) > 0) best = v;
        }
        return best;
    }
}
