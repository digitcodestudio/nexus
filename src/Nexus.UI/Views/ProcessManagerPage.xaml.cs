using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexus.Models;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Views;

public sealed partial class ProcessManagerPage : Page
{
    public ProcessManagerViewModel ViewModel { get; }

    public ProcessManagerPage()
    {
        ViewModel = App.GetRequiredService<ProcessManagerViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private async void ToggleProtect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProcessDisplayRow row })
            await ViewModel.ToggleProtectionAsync(row);
    }

    private async void EcoQoS_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProcessDisplayRow row })
            await ViewModel.ExecuteActionAsync(row, SafeActionType.ApplyEcoQoS, confirmed: true);
    }

    private async void GracefulClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProcessDisplayRow row }) return;

        var dlg = new ContentDialog
        {
            Title = "Konfirmasi Tutup Halus (WM_CLOSE)",
            Content = $"Kirim permintaan tutup halus ke '{row.DisplayName}' (PID {row.ProcessId})? Aplikasi masih diberi kesempatan menyimpan pekerjaan sebelum menutup.",
            PrimaryButtonText = "Ya, Tutup Halus",
            CloseButtonText = "Batal",
            XamlRoot = XamlRoot
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ExecuteActionAsync(row, SafeActionType.GracefulClose, confirmed: true);
        }
    }
}

