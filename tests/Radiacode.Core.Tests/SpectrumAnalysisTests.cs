using Radiacode.Core;

namespace Radiacode.Core.Tests;

public class SpectrumAnalysisTests
{
    /// <summary>Flat continuum (100 counts/channel, 3 keV/channel) with one Gaussian peak at the K-40 line.</summary>
    private static Spectrum SyntheticK40()
    {
        var counts = new int[1024];
        double sigmaKeV = SpectrumAnalysis.FwhmKeV(1460.8) / 2.355;
        for (int ch = 0; ch < counts.Length; ch++)
        {
            double e = 3.0 * ch;
            double peak = 600 * Math.Exp(-0.5 * Math.Pow((e - 1460.8) / sigmaKeV, 2));
            counts[ch] = (int)Math.Round(100 + peak);
        }
        return new Spectrum(TimeSpan.FromHours(1), 0f, 3f, 0f, counts);
    }

    [Fact]
    public void PeakOnFlatContinuum_IsDetected()
    {
        var s = SyntheticK40();
        var k40 = SpectrumAnalysis.Analyze(s, SpectrumAnalysis.CommonLines.First(l => l.Nuclide == "K-40"));
        Assert.True(k40.Detected);
        Assert.True(k40.Net > 1000);
    }

    [Fact]
    public void FlatContinuum_WithoutPeak_IsNotDetected()
    {
        var s = SyntheticK40();
        var cs = SpectrumAnalysis.Analyze(s, SpectrumAnalysis.CommonLines.First(l => l.Nuclide == "Cs-137"));
        Assert.True(cs.InRange);
        Assert.False(cs.Detected);
        Assert.InRange(cs.Net, -50, 50);
    }

    [Fact]
    public void LineOutsideSpectrum_IsNotInRange()
    {
        var s = new Spectrum(TimeSpan.FromHours(1), 0f, 1f, 0f, Enumerable.Repeat(10, 1024).ToArray()); // 0–1023 keV
        var tl = SpectrumAnalysis.Analyze(s, SpectrumAnalysis.CommonLines.First(l => l.Nuclide == "Tl-208"));
        Assert.False(tl.InRange);
    }

    [Fact]
    public void LoneCo60_1173_IsNotReportedWithoutItsPartner()
    {
        // Peak only at 1173 keV (as a blended Bi-214 hump would look), nothing at 1332 keV.
        var counts = new int[1024];
        double sigma = SpectrumAnalysis.FwhmKeV(1173.2) / 2.355;
        for (int ch = 0; ch < counts.Length; ch++)
        {
            double e = 3.0 * ch;
            counts[ch] = (int)Math.Round(100 + 600 * Math.Exp(-0.5 * Math.Pow((e - 1173.2) / sigma, 2)));
        }
        var results = SpectrumAnalysis.AnalyzeAll(new Spectrum(TimeSpan.FromHours(1), 0f, 3f, 0f, counts));
        var co1173 = results.First(r => r.Line.Nuclide == "Co-60" && r.Line.EnergyKeV < 1200);
        Assert.True(co1173.Significance >= 3);
        Assert.False(co1173.Detected);
        Assert.NotNull(co1173.Note);
    }
}