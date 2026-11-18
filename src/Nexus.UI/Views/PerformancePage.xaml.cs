using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Views;

public sealed partial class PerformancePage : Page
{
    public PerformanceViewModel ViewModel { get; }

    public PerformancePage()
    {
        ViewModel = App.GetRequiredService<PerformanceViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.AttachDispatcher(DispatcherQueue);
    }
}

