using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexus.Storage;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        string dbPath = App.GetRequiredService<SqliteNexusRepository>().DatabasePath;
        await ViewModel.LoadAsync(App.ActiveBackdropDescription, dbPath);
    }
}
