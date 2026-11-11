using Radiacode.App.ViewModels;

namespace Radiacode.App.Views;

public sealed partial class SpectrumView : UserControl
{
    public MainViewModel ViewModel { get; set; } = null!;

    public SpectrumView() => InitializeComponent();

    private async void SaveBackground_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Save as background?",
                "The current spectrum becomes the reference that later measurements are compared with (replacing any saved background). Use a long measurement in a normal place without sources.",
                "Save background"))
            ViewModel.SaveBackgroundCommand.Execute(null);
    }

    private async void ResetSpectrum_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Reset spectrum?",
                "This clears the accumulated spectrum on the device. Export it first if you want to keep it.", "Reset"))
            await ViewModel.ResetSpectrumCommand.ExecuteAsync(null);
    }
}
