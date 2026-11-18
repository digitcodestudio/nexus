using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Views;

public sealed partial class MemoryOptimizerPage : Page
{
    public MemoryOptimizerViewModel ViewModel { get; }

    public MemoryOptimizerPage()
    {
        ViewModel = App.GetRequiredService<MemoryOptimizerViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private async void ApplyEcoQoS_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MemoryConsumerRow row })
        {
            await ViewModel.ApplyEcoQoSForGroupAsync(row);
        }
    }

    private async void ApplyScopedTrim_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MemoryConsumerRow row }) return;

        var dlg = new ContentDialog
        {
            Title = "Konfirmasi Optimasi Memori Terarah",
            Content = $"Jalankan optimasi working set terarah hanya untuk '{row.DisplayName}' (PID {row.RepresentativePid})? Proses tidak akan ditutup dan data Anda tetap aman.",
            PrimaryButtonText = "Ya, Optimalkan",
            CloseButtonText = "Batal",
            XamlRoot = XamlRoot
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ApplyScopedTrimAsync(row);
        }
    }
}

