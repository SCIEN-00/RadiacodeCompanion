namespace Radiacode.Core;

/// <summary>A gamma line worth watching for in a field spectrum.</summary>
/// <summary>A gamma line worth watching for. <paramref name="PartnerKeV"/>: another line of the same nuclide that must also show up.</summary>
public sealed record GammaLine(string Nuclide, double EnergyKeV, string Origin, double? PartnerKeV = null);

/// <summary>Region-of-interest result for one line: gross counts, side-band background, net and significance.</summary>
public sealed record LineResult(GammaLine Line, double Gross, double Background, double Net, double Significance, string? Note = null)
{
    /// <summary>True when the window is inside the spectrum and could be evaluated.</summary>
    public bool InRange => Gross > 0;

    /// <summary>Net excess of at least 3σ over the local continuum.</summary>
    public bool Detected => InRange && Significance >= 3 && Note is null;
}

/// <summary>
/// Simple, indicative peak check: counts in a window of ±0.6·FWHM around the line, background estimated from
/// two adjacent side bands (half the window width each). Good enough to say "K-40 is clearly there" or
/// "no Cs-137 above background"; not a replacement for proper peak fitting.
/// </summary>
public static class SpectrumAnalysis
{
    public static readonly GammaLine[] CommonLines =
    [
        // Am-241 (59.5 keV) is deliberately absent: it sits on the low-energy scatter hump, where the
        // side-band background estimate is invalid and gives false positives.
        new("Ra-226", 186.2, "uranium chain"),
        new("Pb-214", 351.9, "radon progeny"),
        new("I-131", 364.5, "medical"),
        new("Bi-214", 609.3, "radon progeny"),
        new("Cs-137", 661.7, "fallout / fission"),
        new("Ac-228", 911.2, "thorium chain"),
        new("Bi-214", 1120.3, "radon progeny"),
        new("Co-60", 1173.2, "industrial", PartnerKeV: 1332.5),
        new("Co-60", 1332.5, "industrial", PartnerKeV: 1173.2),
        new("K-40", 1460.8, "natural potassium"),
        new("Bi-214", 1764.5, "radon progeny"),
        new("Tl-208", 2614.5, "thorium chain"),
    ];

    /// <summary>Approximate FWHM for a small CsI(Tl) detector (~9 % at 662 keV, scaling with √E).</summary>
    public static double FwhmKeV(double energyKeV, double resolutionAt662 = 0.09) =>
        resolutionAt662 * 661.7 * Math.Sqrt(Math.Max(energyKeV, 1) / 661.7);

    public static LineResult Analyze(Spectrum s, GammaLine line, double resolutionAt662 = 0.09)
    {
        double fwhm = FwhmKeV(line.EnergyKeV, resolutionAt662);
        int lo = (int)Math.Round(s.ChannelOf(line.EnergyKeV - 0.6 * fwhm));
        int hi = (int)Math.Round(s.ChannelOf(line.EnergyKeV + 0.6 * fwhm));
        int n = hi - lo + 1;
        int w = Math.Max(1, n / 2);
        if (n < 2 || lo - w < 1 || hi + w >= s.Counts.Length)
            return new LineResult(line, 0, 0, 0, 0);

        double gross = Sum(s.Counts, lo, hi);
        double sides = Sum(s.Counts, lo - w, lo - 1) + Sum(s.Counts, hi + 1, hi + w);
        double scale = n / (2.0 * w);
        double background = sides * scale;
        double net = gross - background;
        double variance = gross + sides * scale * scale;
        double significance = variance > 0 ? net / Math.Sqrt(variance) : 0;
        return new LineResult(line, gross, background, net, significance);
    }

    /// <summary>
    /// Analyzes all common lines. A line whose nuclide has a second strong line (e.g. Co-60 1173 + 1332 keV) only counts
    /// when the partner also rises above background; otherwise a lone bump is more likely a blend of other lines
    /// (Bi-214 1120/1155/1238 keV smear into one hump on a small scintillator).
    /// </summary>
    public static IReadOnlyList<LineResult> AnalyzeAll(Spectrum s)
    {
        var results = CommonLines.Select(l => Analyze(s, l)).ToArray();
        for (int i = 0; i < results.Length; i++)
        {
            var r = results[i];
            if (r.Line.PartnerKeV is not { } partnerKeV || r.Significance < 3) continue;
            var partner = results.FirstOrDefault(p => p.Line.Nuclide == r.Line.Nuclide && Math.Abs(p.Line.EnergyKeV - partnerKeV) < 1);
            if (partner is not null && partner.InRange && partner.Significance < 2)
                results[i] = r with { Note = $"{partnerKeV:0} keV line missing" };
        }
        return results;
    }

    private static double Sum(int[] counts, int from, int to)
    {
        double sum = 0;
        for (int i = from; i <= to; i++) sum += counts[i];
        return sum;
    }
}
