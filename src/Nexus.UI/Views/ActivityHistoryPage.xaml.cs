using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Views;

public sealed partial class ActivityHistoryPage : Page
{
    public ActivityHistoryViewModel ViewModel { get; }

    public ActivityHistoryPage()
    {
        ViewModel = App.GetRequiredService<ActivityHistoryViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BackupRow row })
        {
            await ViewModel.RollbackAsync(row);
        }
    }
}

