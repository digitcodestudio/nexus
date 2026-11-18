using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Views;

public sealed partial class SmartAutomationPage : Page
{
    public SmartAutomationViewModel ViewModel { get; }

    public SmartAutomationPage()
    {
        ViewModel = App.GetRequiredService<SmartAutomationViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.EvaluateNowAsync();
    }
}

