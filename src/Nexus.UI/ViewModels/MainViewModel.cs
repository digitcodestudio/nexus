using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Nexus.Core.Contracts;
using Nexus.Core.Formatting;
using Nexus.Models;
using Nexus.Optimizer;

namespace Nexus.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISystemMonitorService _monitor;
    private readonly OptimizationEngine _optimizer;
    private DispatcherQueue? _dispatcher;

    [ObservableProperty] public partial string ActiveTabTag { get; set; } = "Overview";
    [ObservableProperty] public partial string ActiveProfileName { get; set; } = "Seimbang";
    [ObservableProperty] public partial string AutomationStatusText { get; set; } = "Mode Aman · Aktif";
    [ObservableProperty] public partial bool IsSafeMode { get; set; } = true;
    [ObservableProperty] public partial bool IsAutomationPaused { get; set; }
    [ObservableProperty] public partial bool IsRibbonExpanded { get; set; } = true;
    [ObservableProperty] public partial string RibbonBannerMessage { get; set; } = string.Empty;

    [ObservableProperty] public partial string StatusBarRamText { get; set; } = "RAM: —";
    [ObservableProperty] public partial string StatusBarCpuText { get; set; } = "CPU: —";
    [ObservableProperty] public partial string StatusBarSwapText { get; set; } = "Komit: —";
    [ObservableProperty] public partial string StatusBarCountsText { get; set; } = "Proses: — · Layanan: —";
    [ObservableProperty] public partial string StatusBarGuardrailText { get; set; } = "Batas Target: RAM ≤ 75% · CPU ≤ 65%";
    [ObservableProperty] public partial string TrayTooltipText { get; set; } = "NEXUS System Optimizer — Berjalan di latar belakang";

    public event EventHandler<string>? NavigationRequested;
    public event EventHandler? HideToTrayRequested;
    public event EventHandler? RefreshRequested;

    public MainViewModel(ISystemMonitorService monitor, OptimizationEngine optimizer)
    {
        _monitor = monitor;
        _optimizer = optimizer;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _optimizer.ProfileChanged += OnProfileChanged;
    }

    public void AttachDispatcher(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        SyncOptimizerState();
        if (_monitor.LatestSnapshot is not null)
        {
            ApplySnapshot(_monitor.LatestSnapshot);
        }
    }

    public void SyncOptimizerState()
    {
        ActiveProfileName = TranslateProfileName(_optimizer.ActiveProfile.Type);
        IsSafeMode = _optimizer.IsSafeModeEnabled;
        IsAutomationPaused = _optimizer.IsAutomationPaused;
        AutomationStatusText = IsAutomationPaused
            ? "Dijeda"
            : IsSafeMode
                ? "Mode Aman · Pantau"
                : "Otomatisasi Aktif";
    }

    [RelayCommand]
    private void SelectTab(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        ActiveTabTag = tag;
        NavigationRequested?.Invoke(this, tag);
    }

    [RelayCommand]
    private async Task SwitchProfileAsync(string profileTypeStr)
    {
        if (!Enum.TryParse<ProfileType>(profileTypeStr, out var type)) return;
        await _optimizer.SetActiveProfileAsync(type);
        SyncOptimizerState();
        RibbonBannerMessage = $"Profil aktif diubah ke '{ActiveProfileName}' (RAM ≤ {_monitor.RamGuardrailPercent:F0}%, CPU ≤ {_monitor.CpuGuardrailPercent:F0}%).";
    }

    [RelayCommand]
    private async Task ToggleSafeModeAsync()
    {
        _optimizer.IsSafeModeEnabled = !_optimizer.IsSafeModeEnabled;
        SyncOptimizerState();
        RibbonBannerMessage = _optimizer.IsSafeModeEnabled
            ? "Mode Aman diaktifkan: NEXUS hanya memberi rekomendasi tanpa aksi otomatis."
            : "Mode Otomatisasi aktif: NEXUS dapat menerapkan EcoQoS reversibel saat RAM/CPU melebihi batas.";
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task EmergencyStopAsync()
    {
        await _optimizer.EmergencyStopAsync();
        SyncOptimizerState();
        RibbonBannerMessage = "STOP DARURAT aktif: semua otomatisasi dijeda dan semua proses EcoQoS dikembalikan ke normal.";
    }

    [RelayCommand]
    private async Task CleanTrayNowAsync()
    {
        var report = await _optimizer.CleanTrayAndDuplicatesAsync();
        RibbonBannerMessage = report.Summary;
    }

    [RelayCommand]
    private async Task RunQuickAnalysisAsync()
    {
        var recs = await _optimizer.EvaluateRecommendationsAsync();
        RibbonBannerMessage = recs.Count == 0
            ? "Analisa selesai: sistem sehat dan berada di dalam batas RAM ≤ 75% & CPU ≤ 65%."
            : $"Analisa selesai: ditemukan {recs.Count} rekomendasi optimasi aman. Lihat tab Otomatisasi / Optimasi RAM.";
    }

    [RelayCommand]
    private void ToggleRibbon() => IsRibbonExpanded = !IsRibbonExpanded;

    [RelayCommand]
    private void RequestRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void RequestHideToTray() => HideToTrayRequested?.Invoke(this, EventArgs.Empty);

    private void OnProfileChanged(object? sender, OptimizationProfile profile)
    {
        _dispatcher?.TryEnqueue(SyncOptimizerState);
    }

    private void OnSnapshotUpdated(object? sender, SystemSnapshot snapshot)
    {
        _dispatcher?.TryEnqueue(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(SystemSnapshot s)
    {
        StatusBarRamText = $"RAM: {ByteFormatter.FormatBytes(s.Memory.UsedBytes)} ({s.Memory.UsagePercent:F1}%)";
        StatusBarCpuText = $"CPU: {s.Cpu.TotalUsagePercent:F1}%";
        StatusBarSwapText = $"Komit: {ByteFormatter.FormatBytes(s.Memory.CommittedBytes)}";
        StatusBarCountsText = $"Proses: {s.RunningProcessCount} · Layanan: {s.RunningServiceCount} berjalan";
        StatusBarGuardrailText = $"Batas Target: RAM ≤ {_monitor.RamGuardrailPercent:F0}% · CPU ≤ {_monitor.CpuGuardrailPercent:F0}%";
        TrayTooltipText = $"NEXUS · CPU {s.Cpu.TotalUsagePercent:F0}% · RAM {s.Memory.UsagePercent:F0}% ({ActiveProfileName})";
    }

    public static string TranslateProfileName(ProfileType type) => type switch
    {
        ProfileType.Balanced => "Seimbang",
        ProfileType.Development => "Pengembangan",
        ProfileType.HeavyWorkload => "Beban Berat",
        ProfileType.PowerSaving => "Hemat Daya",
        _ => type.ToString()
    };

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        _optimizer.ProfileChanged -= OnProfileChanged;
    }
}
