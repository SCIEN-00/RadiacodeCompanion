namespace Radiacode.Core;

/// <summary>Result of locating the K-40 line: where it was found and the gain factor that moves it to 1460.8 keV.</summary>
public sealed record GainFix(double FoundKeV, double Factor, double Significance)
{
    public Spectrum Apply(Spectrum s) => s with { A0 = (float)(s.A0 * Factor), A1 = (float)(s.A1 * Factor), A2 = (float)(s.A2 * Factor) };
}

/// <summary>
/// Scintillator gain drifts with temperature (a few % in the cold), which shifts every peak. K-40 at 1460.8 keV is
/// present in practically every background spectrum, so its measured position gives a self-calibration factor.
/// </summary>
public static class GainCorrection
{
    public const double K40KeV = 1460.8;
    private const double SearchFraction = 0.10;   // look within ±10 %
    private const double MinSignificance = 5.0;    // only trust a clear peak

    public static GainFix? FindK40(Spectrum s)
    {
        int n = s.Counts.Length;
        int lo = (int)Math.Floor(s.ChannelOf(K40KeV * (1 - SearchFraction)));
        int hi = (int)Math.Ceiling(s.ChannelOf(K40KeV * (1 + SearchFraction)));
        double fwhmCh = Math.Max(2, s.ChannelOf(K40KeV + SpectrumAnalysis.FwhmKeV(K40KeV) / 2) - s.ChannelOf(K40KeV - SpectrumAnalysis.FwhmKeV(K40KeV) / 2));
        int w = (int)Math.Ceiling(fwhmCh);
        if (lo - w < 2 || hi + w > n - 3) return null;

        // 5-channel moving average, then "net" = value minus the straight line between points one FWHM to each side.
        double Smooth(int c)
        {
            double sum = 0;
            for (int k = -2; k <= 2; k++) sum += s.Counts[c + k];
            return sum / 5;
        }
        int best = -1;
        double bestNet = 0;
        for (int c = lo; c <= hi; c++)
        {
            double net = Smooth(c) - (Smooth(c - w) + Smooth(c + w)) / 2;
            if (net > bestNet) { bestNet = net; best = c; }
        }
        if (best < 0) return null;

        // Centroid over ±FWHM/2 above the local baseline.
        int half = Math.Max(1, w / 2);
        double baseline = (Smooth(best - w) + Smooth(best + w)) / 2;
        double sw = 0, sx = 0;
        for (int c = best - half; c <= best + half; c++)
        {
            double v = Math.Max(0, s.Counts[c] - baseline);
            sw += v;
            sx += v * c;
        }
        double centroid = sw > 0 ? sx / sw : best;
        double foundKeV = s.EnergyKeV(centroid);

        var check = SpectrumAnalysis.Analyze(s, new GammaLine("K-40", foundKeV, "natural potassium"));
        if (check.Significance < MinSignificance) return null;
        return new GainFix(foundKeV, K40KeV / foundKeV, check.Significance);
    }
}
