using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Views;

public sealed partial class CpuOptimizerPage : Page
{
    public CpuOptimizerViewModel ViewModel { get; }

    public CpuOptimizerPage()
    {
        ViewModel = App.GetRequiredService<CpuOptimizerViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private async void EnableEco_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CpuConsumerRow row })
            await ViewModel.ToggleEcoQoSForGroupAsync(row, enable: true);
    }

    private async void DisableEco_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CpuConsumerRow row })
            await ViewModel.ToggleEcoQoSForGroupAsync(row, enable: false);
    }
}

