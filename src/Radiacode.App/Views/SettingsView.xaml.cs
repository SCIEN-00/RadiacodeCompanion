using Radiacode.App.ViewModels;

namespace Radiacode.App.Views;

public sealed partial class SettingsView : UserControl
{
    public MainViewModel ViewModel { get; set; } = null!;

    public SettingsView() => InitializeComponent();
}
