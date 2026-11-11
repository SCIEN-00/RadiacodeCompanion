using Radiacode.App.ViewModels;

namespace Radiacode.App.Views;

public sealed partial class HistoryView : UserControl
{
    public MainViewModel ViewModel { get; set; } = null!;

    public HistoryView() => InitializeComponent();
}
