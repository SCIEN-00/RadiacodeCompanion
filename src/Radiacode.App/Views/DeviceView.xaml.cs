using Radiacode.App.ViewModels;

namespace Radiacode.App.Views;

public sealed partial class DeviceView : UserControl
{
    public MainViewModel ViewModel { get; set; } = null!;

    public DeviceView() => InitializeComponent();

    private async void ResetSpectrum_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Reset spectrum?",
                "This clears the accumulated spectrum on the device. Export it first if you want to keep it.", "Reset"))
            await ViewModel.ResetSpectrumCommand.ExecuteAsync(null);
    }

    private async void ResetDose_Click(object sender, RoutedEventArgs e)
    {
        if (await Dialogs.ConfirmAsync(XamlRoot, "Reset accumulated dose?",
                "This sets the dose counter on the device back to zero. It cannot be undone.", "Reset dose"))
            await ViewModel.ResetDoseCommand.ExecuteAsync(null);
    }
}
