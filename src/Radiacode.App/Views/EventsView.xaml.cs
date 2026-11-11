using Radiacode.App.ViewModels;

namespace Radiacode.App.Views;

public sealed partial class EventsView : UserControl
{
    public MainViewModel ViewModel { get; set; } = null!;

    public EventsView() => InitializeComponent();
}
