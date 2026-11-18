using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Nexus.UI.Helpers;
using Nexus.UI.Services;
using Nexus.UI.ViewModels;
using Nexus.UI.Views;

namespace Nexus.UI;

public sealed partial class MainWindow : Window
{
    private readonly SettingsViewModel _settingsVm;
    private bool _forceExit;

    public MainViewModel ViewModel { get; }
    public IRelayCommand ShowWindowCommand { get; }

    public MainWindow(MainViewModel viewModel, SettingsViewModel settingsVm, ThemeService themeService)
    {
        ViewModel = viewModel;
        _settingsVm = settingsVm;
        ShowWindowCommand = new RelayCommand(RestoreFromTray);

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ApplyBrandIcons();
        App.ActiveBackdropDescription = BackdropHelper.ApplyOptimalBackdrop(this);
        themeService.AttachRoot(RootContainer);
        ViewModel.AttachDispatcher(DispatcherQueue);

        ViewModel.NavigationRequested += (_, tag) => NavigateToTab(tag);
        ViewModel.HideToTrayRequested += (_, _) => HideToTray();
        ViewModel.RefreshRequested += (_, _) => RefreshCurrentPage();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1380, 880));
        AppWindow.Closing += OnAppWindowClosing;

        NavigateToTab("Overview");
    }

    private void ApplyBrandIcons()
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(iconPath))
            {
                // Window & Taskbar Icon
                AppWindow.SetIcon(iconPath);

                // Brand Emblem in Custom Title Bar
                var bitmap = new BitmapImage(new Uri(iconPath));
                BrandLogoImage.Source = bitmap;

                // System Tray Icon
                NexusTrayIcon.IconSource = bitmap;
                NexusTrayIcon.ForceCreate();
            }
        }
        catch
        {
            // Continue gracefully if icon cannot be loaded
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_forceExit && _settingsVm.MinimizeToTrayOnClose)
        {
            args.Cancel = true;
            HideToTray();
        }
        else
        {
            try { NexusTrayIcon.Dispose(); } catch { }
        }
    }

    public void HideToTray()
    {
        AppWindow.Hide();
    }

    public void RestoreFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    private void RibbonTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            ViewModel.SelectTabCommand.Execute(tag);
        }
    }

    private void NavigateToTab(string tag)
    {
        try
        {
            Type targetPage = tag switch
            {
                "Overview" => typeof(OverviewPage),
                "Performance" => typeof(PerformancePage),
                "MemoryOptimizer" => typeof(MemoryOptimizerPage),
                "CpuOptimizer" => typeof(CpuOptimizerPage),
                "ProcessManager" => typeof(ProcessManagerPage),
                "ServiceManager" => typeof(ServiceManagerPage),
                "SmartAutomation" => typeof(SmartAutomationPage),
                "OptimizationProfiles" => typeof(ProfilesPage),
                "ActivityHistory" => typeof(ActivityHistoryPage),
                "Settings" => typeof(SettingsPage),
                _ => typeof(OverviewPage)
            };

            if (ContentFrame.CurrentSourcePageType != targetPage)
            {
                ContentFrame.Navigate(targetPage);
            }

            UpdateRibbonTabVisualState(tag);
        }
        catch (Exception ex)
        {
            ViewModel.RibbonBannerMessage = $"Gagal membuka halaman: {ex.Message}";
        }
    }

    private void UpdateRibbonTabVisualState(string activeTag)
    {
        bool isLight = RootContainer.ActualTheme == ElementTheme.Light;
        var accent = new SolidColorBrush(isLight
            ? Windows.UI.Color.FromArgb(255, 0, 103, 192)
            : Windows.UI.Color.FromArgb(255, 76, 194, 255));
        var primary = new SolidColorBrush(isLight
            ? Windows.UI.Color.FromArgb(255, 17, 24, 39)
            : Windows.UI.Color.FromArgb(255, 240, 244, 248));
        var secondary = new SolidColorBrush(isLight
            ? Windows.UI.Color.FromArgb(255, 71, 85, 105)
            : Windows.UI.Color.FromArgb(255, 148, 163, 184));
        var transparent = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        foreach (var child in RibbonTabsPanel.Children)
        {
            if (child is Button btn && btn.Tag is string tag)
            {
                bool isActive = tag.Equals(activeTag, StringComparison.OrdinalIgnoreCase);
                btn.BorderBrush = isActive ? accent : transparent;
                btn.Foreground = isActive ? primary : secondary;
                btn.FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            }
        }
    }

    private void RefreshCurrentPage()
    {
        switch (ContentFrame.Content)
        {
            case MemoryOptimizerPage p: _ = p.ViewModel.RefreshAsync(); break;
            case CpuOptimizerPage p: _ = p.ViewModel.RefreshAsync(); break;
            case ProcessManagerPage p: _ = p.ViewModel.RefreshAsync(); break;
            case ServiceManagerPage p: _ = p.ViewModel.RefreshAsync(); break;
            case SmartAutomationPage p: _ = p.ViewModel.EvaluateNowAsync(); break;
            case ProfilesPage p: _ = p.ViewModel.LoadAsync(); break;
            case ActivityHistoryPage p: _ = p.ViewModel.RefreshAsync(); break;
        }
        ViewModel.RibbonBannerMessage = "Data halaman diperbarui.";
    }

    private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        e.Handled = true;
        ViewModel.RibbonBannerMessage = $"Navigasi dicegah dengan aman: {e.Exception?.Message}";
    }

    // System Tray Menu Handlers
    private void TrayOpen_Click(object sender, RoutedEventArgs e) => RestoreFromTray();
    private void TrayProfileBalanced_Click(object sender, RoutedEventArgs e) => _ = ViewModel.SwitchProfileCommand.ExecuteAsync("Balanced");
    private void TrayProfileDev_Click(object sender, RoutedEventArgs e) => _ = ViewModel.SwitchProfileCommand.ExecuteAsync("Development");
    private void TrayProfileHeavy_Click(object sender, RoutedEventArgs e) => _ = ViewModel.SwitchProfileCommand.ExecuteAsync("HeavyWorkload");
    private void TrayProfilePower_Click(object sender, RoutedEventArgs e) => _ = ViewModel.SwitchProfileCommand.ExecuteAsync("PowerSaving");
    private void TrayEmergencyStop_Click(object sender, RoutedEventArgs e) => _ = ViewModel.EmergencyStopCommand.ExecuteAsync(null);

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _forceExit = true;
        try { NexusTrayIcon.Dispose(); } catch { }
        Close();
    }
}
