using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexus.Models;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Views;

public sealed partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; }

    public ProfilesPage()
    {
        ViewModel = App.GetRequiredService<ProfilesViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private async void ActivateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProfileType type })
        {
            await ViewModel.ActivateProfileAsync(type);
        }
    }
}

