using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Radiacode.App.ViewModels;
using Windows.UI;

namespace Radiacode.App;

/// <summary>
/// Glow behind the dose-rate number: composition drop shadows shaped by the text (no offset), so they read as light
/// around the digits. Hue follows the dose rate smoothly (green → yellow → orange → red on a log scale), the halo size
/// follows the count rate, and the brightness pulses gently while live, faster at higher dose rates.
/// </summary>
public sealed partial class MainPage
{
    // colour stops on the dose rate (nSv/h); darker shades on a light background so the glow stays visible
    private static readonly double[] GlowStopsNSvH = [100, 300, 1_000, 10_000];
    private static readonly Color[] GlowDark = [C(0x22, 0xC5, 0x5E), C(0xFA, 0xCC, 0x15), C(0xF9, 0x73, 0x16), C(0xEF, 0x44, 0x44)];
    private static readonly Color[] GlowLight = [C(0x16, 0xA3, 0x4A), C(0xCA, 0x8A, 0x04), C(0xEA, 0x58, 0x0C), C(0xDC, 0x26, 0x26)];
    private static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    private Compositor? _glowCompositor;
    private DropShadow? _glowHalo;   // wide halo: radius follows the count rate
    private DropShadow? _glowCore;   // tight bright rim around the digits
    private double _glowPulseSeconds;
    private bool _glowPulsing;

    private bool GlowReady => _glowCompositor is not null && _glowHalo is not null && _glowCore is not null;

    /// <summary>0 at the first stop (and below), 1 at the last stop (and above), linear in log(dose rate).</summary>
    private static double DosePosition(double nSvH)
    {
        double lo = Math.Log10(GlowStopsNSvH[0]), hi = Math.Log10(GlowStopsNSvH[^1]);
        return Math.Clamp((Math.Log10(Math.Max(nSvH, 1)) - lo) / (hi - lo), 0, 1);
    }

    private static Color DoseColor(double nSvH, Color[] palette)
    {
        double v = Math.Max(nSvH, 1);
        if (v <= GlowStopsNSvH[0]) return palette[0];
        for (int i = 1; i < GlowStopsNSvH.Length; i++)
        {
            if (v > GlowStopsNSvH[i]) continue;
            double f = (Math.Log10(v) - Math.Log10(GlowStopsNSvH[i - 1])) / (Math.Log10(GlowStopsNSvH[i]) - Math.Log10(GlowStopsNSvH[i - 1]));
            Color a = palette[i - 1], b = palette[i];
            byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * f);
            return Color.FromArgb(255, L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
        }
        return palette[^1];
    }

    private void SetupDoseGlow()
    {
        if (GlowReady) return;
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(DoseGlowHost).Compositor;
            var mask = DoseValueText.GetAlphaMask();
            var container = compositor.CreateContainerVisual();
            DropShadow Layer(float blur)
            {
                var shadow = compositor.CreateDropShadow();
                shadow.Mask = mask;
                shadow.Offset = Vector3.Zero;
                shadow.BlurRadius = blur;
                shadow.Opacity = 0f;
                var sprite = compositor.CreateSpriteVisual();
                sprite.RelativeSizeAdjustment = Vector2.One;
                sprite.Shadow = shadow;
                container.Children.InsertAtTop(sprite);
                return shadow;
            }
            _glowHalo = Layer(16f);
            _glowCore = Layer(5f);
            container.Size = new Vector2((float)DoseValueText.ActualWidth, (float)DoseValueText.ActualHeight);
            ElementCompositionPreview.SetElementChildVisual(DoseGlowHost, container);
            DoseValueText.SizeChanged += (_, e) => container.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            _glowCompositor = compositor;
        }
        catch
        {
            _glowHalo = _glowCore = null; // composition shadows not available here: the number simply has no glow
        }
        UpdateDoseGlow();
    }

    private void UpdateDoseGlow()
    {
        bool hasReading = ViewModel.DoseLevelIndex >= 0;
        double dose = ViewModel.LiveDoseNSvH;
        var palette = ActualTheme == ElementTheme.Light ? GlowLight : GlowDark;
        var color = DoseColor(dose, palette);
        DoseStateText.Foreground = hasReading
            ? new SolidColorBrush(color)
            : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        if (!GlowReady) return;
        try
        {
            if (!hasReading || !ViewModel.IsConnected)
            {
                StopGlowPulse();
                _glowHalo!.Opacity = _glowCore!.Opacity = 0f;
                return;
            }

            // hue glides to the new colour instead of jumping
            foreach (var shadow in new[] { _glowHalo!, _glowCore! })
            {
                var tint = _glowCompositor!.CreateColorKeyFrameAnimation();
                tint.InsertKeyFrame(1f, color);
                tint.Duration = TimeSpan.FromMilliseconds(900);
                shadow.StartAnimation(nameof(DropShadow.Color), tint);
            }

            // halo size follows the count rate: ~10 cps background → 18 px, 100 cps → 25 px, 1000 cps → 35 px, max 48 px
            double cps = Math.Max(0, ViewModel.LiveCountRate);
            float radius = (float)Math.Clamp(15 + 10 * Math.Log10(1 + cps / 10), 15, 48);
            var grow = _glowCompositor!.CreateScalarKeyFrameAnimation();
            grow.InsertKeyFrame(1f, radius);
            grow.Duration = TimeSpan.FromMilliseconds(900);
            _glowHalo!.StartAnimation(nameof(DropShadow.BlurRadius), grow);

            if (ViewModel.PulseActive)
            {
                // breathing period 2.6 s at background, down to 0.8 s at 10 µSv/h and above
                double seconds = 2.6 - 1.8 * DosePosition(dose);
                if (!_glowPulsing || Math.Abs(seconds - _glowPulseSeconds) > _glowPulseSeconds * 0.15) StartGlowPulse(seconds);
            }
            else
            {
                StopGlowPulse();
                _glowHalo.Opacity = _glowCore!.Opacity = 0.85f;
            }
        }
        catch
        {
            // animation not supported on this platform: keep whatever was applied
        }
    }

    /// <summary>Brightness breathes between dim and full, same hue.</summary>
    private void StartGlowPulse(double seconds)
    {
        if (!GlowReady) return;
        ScalarKeyFrameAnimation Pulse(float low, float high)
        {
            var pulse = _glowCompositor!.CreateScalarKeyFrameAnimation();
            var ease = _glowCompositor.CreateCubicBezierEasingFunction(new Vector2(0.45f, 0f), new Vector2(0.55f, 1f));
            pulse.InsertKeyFrame(0f, low, ease);
            pulse.InsertKeyFrame(0.5f, high, ease);
            pulse.InsertKeyFrame(1f, low, ease);
            pulse.Duration = TimeSpan.FromSeconds(seconds);
            pulse.IterationBehavior = AnimationIterationBehavior.Forever;
            return pulse;
        }
        _glowHalo!.StartAnimation(nameof(DropShadow.Opacity), Pulse(0.35f, 1f));
        _glowCore!.StartAnimation(nameof(DropShadow.Opacity), Pulse(0.6f, 1f));
        _glowPulsing = true;
        _glowPulseSeconds = seconds;
    }

    private void StopGlowPulse()
    {
        if (!_glowPulsing || !GlowReady) return;
        _glowHalo!.StopAnimation(nameof(DropShadow.Opacity));
        _glowCore!.StopAnimation(nameof(DropShadow.Opacity));
        _glowPulsing = false;
    }
}