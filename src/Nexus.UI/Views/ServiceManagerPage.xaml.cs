using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexus.Models;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Views;

public sealed partial class ServiceManagerPage : Page
{
    public ServiceManagerViewModel ViewModel { get; }

    public ServiceManagerPage()
    {
        ViewModel = App.GetRequiredService<ServiceManagerViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await ViewModel.StartSelectedAsync();

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedService is null) return;
        if (await ConfirmAsync("Hentikan Layanan", $"Hentikan layanan '{ViewModel.SelectedService.DisplayName}'? Konfigurasi awal akan dicadangkan otomatis ke SQLite untuk rollback."))
        {
            await ViewModel.StopSelectedAsync(confirmed: true);
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedService is null) return;
        if (await ConfirmAsync("Mulai Ulang Layanan", $"Mulai ulang layanan '{ViewModel.SelectedService.DisplayName}'?"))
        {
            await ViewModel.RestartSelectedAsync(confirmed: true);
        }
    }

    private async void SetManual_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedService is null) return;
        if (await ConfirmAsync("Ubah ke Manual", $"Ubah tipe startup '{ViewModel.SelectedService.DisplayName}' menjadi Manual? Layanan tetap bisa berjalan saat dibutuhkan dan dapat di-rollback 1 klik."))
        {
            await ViewModel.ChangeStartupSelectedAsync(ServiceStartupType.Manual, confirmed: true);
        }
    }

    private async void SetAuto_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedService is null) return;
        if (await ConfirmAsync("Ubah ke Otomatis", $"Ubah tipe startup '{ViewModel.SelectedService.DisplayName}' menjadi Otomatis?"))
        {
            await ViewModel.ChangeStartupSelectedAsync(ServiceStartupType.Automatic, confirmed: true);
        }
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e) => await ViewModel.RollbackSelectedAsync();

    private async Task<bool> ConfirmAsync(string title, string content)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = "Lanjutkan",
            CloseButtonText = "Batal",
            XamlRoot = XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }
}

