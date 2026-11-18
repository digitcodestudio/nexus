using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Nexus.Core.Contracts;
using Nexus.Core.Safety;
using Nexus.UI.Helpers;
using Nexus.UI.Services;

namespace Nexus.UI.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISystemMonitorService _monitor;
    private readonly INexusRepository _repository;
    private readonly ThemeService _themeService;
    private bool _isLoading;

    [ObservableProperty] public partial bool IsDarkTheme { get; set; } = true;
    [ObservableProperty] public partial bool MinimizeToTrayOnClose { get; set; } = true;
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial int SelectedSamplingIndex { get; set; } = 1; // 1000ms default
    [ObservableProperty] public partial double RamGuardrailPercent { get; set; } = GuardrailDefaults.DefaultRamThresholdPercent;
    [ObservableProperty] public partial double CpuGuardrailPercent { get; set; } = GuardrailDefaults.DefaultCpuThresholdPercent;
    [ObservableProperty] public partial string BackdropInfo { get; set; } = "Mendeteksi...";
    [ObservableProperty] public partial string DatabasePathInfo { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;

    public int[] SamplingIntervalsMs { get; } = [500, 1000, 2000, 5000];

    public SettingsViewModel(
        ISystemMonitorService monitor,
        INexusRepository repository,
        ThemeService themeService)
    {
        _monitor = monitor;
        _repository = repository;
        _themeService = themeService;
        IsDarkTheme = themeService.CurrentTheme != ElementTheme.Light;
    }

    public async Task LoadAsync(string backdropInfo, string dbPath)
    {
        _isLoading = true;
        try
        {
            BackdropInfo = backdropInfo;
            DatabasePathInfo = dbPath;
            IsDarkTheme = _themeService.CurrentTheme != ElementTheme.Light;
            StartWithWindows = StartupHelper.IsStartupEnabled();

            string? traySetting = await _repository.GetSettingAsync("ui.minimize_to_tray");
            MinimizeToTrayOnClose = !bool.TryParse(traySetting, out bool parsedTray) || parsedTray;

            // Sinkronkan slider langsung dengan guardrail aktif dari profil / monitor saat ini
            RamGuardrailPercent = Math.Clamp(_monitor.RamGuardrailPercent, 40, 95);
            CpuGuardrailPercent = Math.Clamp(_monitor.CpuGuardrailPercent, 30, 95);

            if (int.TryParse(await _repository.GetSettingAsync("monitor.sampling_ms"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            {
                int idx = Array.IndexOf(SamplingIntervalsMs, ms);
                if (idx >= 0)
                {
                    SelectedSamplingIndex = idx;
                    _monitor.SamplingInterval = TimeSpan.FromMilliseconds(ms);
                }
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (_isLoading) return;
        _ = _themeService.SetThemeAsync(value ? ElementTheme.Dark : ElementTheme.Light);
        StatusMessage = value ? "Beralih ke Tema Gelap (Fluent Dark)." : "Beralih ke Tema Terang (Fluent Light).";
    }

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        if (_isLoading) return;
        _ = _repository.SetSettingAsync("ui.minimize_to_tray", value ? "true" : "false");
        StatusMessage = value
            ? "Saat jendela ditutup (X), NEXUS akan tetap berjalan di System Tray."
            : "Saat jendela ditutup (X), aplikasi akan keluar sepenuhnya.";
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isLoading) return;
        bool ok = StartupHelper.SetStartupEnabled(value);
        StatusMessage = ok
            ? (value ? "NEXUS akan berjalan otomatis saat Windows menyala." : "Startup otomatis dengan Windows dinonaktifkan.")
            : "Gagal memperbarui pendaftaran startup Windows.";
    }

    partial void OnSelectedSamplingIndexChanged(int value)
    {
        if (_isLoading || value < 0 || value >= SamplingIntervalsMs.Length) return;
        int ms = SamplingIntervalsMs[value];
        _monitor.SamplingInterval = TimeSpan.FromMilliseconds(ms);
        _ = _repository.SetSettingAsync("monitor.sampling_ms", ms.ToString(CultureInfo.InvariantCulture));
        StatusMessage = $"Interval pengambilan data diubah ke {ms} ms.";
    }

    partial void OnRamGuardrailPercentChanged(double value)
    {
        if (_isLoading) return;
        double rounded = Math.Round(value);
        _monitor.RamGuardrailPercent = rounded;
        _ = _repository.SetSettingAsync("guardrail.ram", rounded.ToString("F0", CultureInfo.InvariantCulture));
        StatusMessage = $"Batas target RAM diatur ke ≤ {rounded:F0}%.";
    }

    partial void OnCpuGuardrailPercentChanged(double value)
    {
        if (_isLoading) return;
        double rounded = Math.Round(value);
        _monitor.CpuGuardrailPercent = rounded;
        _ = _repository.SetSettingAsync("guardrail.cpu", rounded.ToString("F0", CultureInfo.InvariantCulture));
        StatusMessage = $"Batas target CPU diatur ke ≤ {rounded:F0}%.";
    }
}
