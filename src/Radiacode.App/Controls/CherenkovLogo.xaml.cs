using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Radiacode.App.Controls;

/// <summary>
/// App logo: a Cherenkov cone. While <see cref="IsActive"/> the glow breathes and the wavefronts ripple;
/// <see cref="PulseSpeed"/> scales the rhythm (applied at the end of each cycle, so it never jumps).
/// </summary>
public sealed partial class CherenkovLogo : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(CherenkovLogo),
        new PropertyMetadata(false, (d, _) => ((CherenkovLogo)d).UpdatePulse()));

    public static readonly DependencyProperty PulseSpeedProperty = DependencyProperty.Register(
        nameof(PulseSpeed), typeof(double), typeof(CherenkovLogo), new PropertyMetadata(1.0));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double PulseSpeed
    {
        get => (double)GetValue(PulseSpeedProperty);
        set => SetValue(PulseSpeedProperty, value);
    }

    public CherenkovLogo()
    {
        InitializeComponent();
        Pulse.Completed += (_, _) => UpdatePulse();
        Loaded += (_, _) => UpdatePulse();
        Unloaded += (_, _) => Pulse.Stop();
    }

    private void UpdatePulse()
    {
        Pulse.Stop();
        if (!IsLoaded || !IsActive) return;
        Pulse.SpeedRatio = double.IsFinite(PulseSpeed) && PulseSpeed > 0 ? PulseSpeed : 1.0;
        Pulse.Begin();
    }
}
