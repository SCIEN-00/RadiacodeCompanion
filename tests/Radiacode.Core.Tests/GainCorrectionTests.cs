using Radiacode.Core;

namespace Radiacode.Core.Tests;

public class GainCorrectionTests
{
    /// <summary>Falling continuum with a K-40 peak that the (drifted) calibration places at <paramref name="peakKeV"/>.</summary>
    private static Spectrum Drifted(double peakKeV, double height = 400)
    {
        var counts = new int[1024];
        double sigma = SpectrumAnalysis.FwhmKeV(peakKeV) / 2.355;
        for (int ch = 0; ch < counts.Length; ch++)
        {
            double e = 3.0 * ch + 8;
            double continuum = 20000 * Math.Exp(-e / 350) + 40;
            counts[ch] = (int)Math.Round(continuum + height * Math.Exp(-0.5 * Math.Pow((e - peakKeV) / sigma, 2)));
        }
        return new Spectrum(TimeSpan.FromHours(16), 8f, 3f, 0f, counts);
    }

    [Theory]
    [InlineData(1426.0)]  // cold detector: ~2.4 % low, as seen outdoors
    [InlineData(1460.8)]
    [InlineData(1500.0)]
    public void FindsK40AndCorrectsGain(double drifted)
    {
        var fix = GainCorrection.FindK40(Drifted(drifted));
        Assert.NotNull(fix);
        Assert.InRange(fix!.FoundKeV, drifted - 6, drifted + 6);
        var corrected = fix.Apply(Drifted(drifted));
        Assert.InRange(corrected.EnergyKeV(corrected.ChannelOf(drifted * fix.Factor)), 1450, 1472);
        Assert.InRange(drifted * fix.Factor, 1454, 1468);
    }

    [Fact]
    public void NoPeak_NoCorrection() => Assert.Null(GainCorrection.FindK40(Drifted(1460.8, height: 0)));
}
