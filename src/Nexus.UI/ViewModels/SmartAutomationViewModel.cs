using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Contracts;
using Nexus.Models;
using Nexus.Optimizer;

namespace Nexus.UI.ViewModels;

public sealed partial class SmartAutomationViewModel : ObservableObject
{
    private readonly OptimizationEngine _optimizer;
    private readonly INexusRepository _repository;
    private bool _isSyncing;

    [ObservableProperty] public partial bool IsSafeMode { get; set; } = true;
    [ObservableProperty] public partial bool IsPaused { get; set; }
    [ObservableProperty] public partial bool EnableAutoTrayCleanup { get; set; } = true;
    [ObservableProperty] public partial bool EnableAutoStopUnusedServices { get; set; }
    [ObservableProperty] public partial string ActiveRuleSummary { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public ObservableCollection<OptimizationRecommendation> Recommendations { get; } = [];

    public SmartAutomationViewModel(OptimizationEngine optimizer, INexusRepository repository)
    {
        _optimizer = optimizer;
        _repository = repository;
        SyncState();
    }

    public void SyncState()
    {
        _isSyncing = true;
        try
        {
            IsSafeMode = _optimizer.IsSafeModeEnabled;
            IsPaused = _optimizer.IsAutomationPaused;
            EnableAutoTrayCleanup = _optimizer.EnableAutoTrayCleanup;
            EnableAutoStopUnusedServices = _optimizer.EnableAutoStopUnusedServices;
            var p = _optimizer.ActiveProfile;
            ActiveRuleSummary =
                $"Profil '{MainViewModel.TranslateProfileName(p.Type)}' · Batas RAM ≤ {p.RamThresholdPercent:F0}% · Batas CPU ≤ {p.CpuThresholdPercent:F0}% · Batas Idle {p.IdleTimeoutMinutes} menit.";
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnIsSafeModeChanged(bool value)
    {
        if (_isSyncing) return;
        _optimizer.IsSafeModeEnabled = value;
        _ = _repository.SetSettingAsync("automation.safe_mode", value ? "true" : "false");
        StatusMessage = value
            ? "Mode Aman aktif: otomatisasi hanya memberi rekomendasi tanpa mengeksekusi perubahan sistem."
            : "Mode Otomatisasi aktif: NEXUS dapat menerapkan EcoQoS reversibel, membersihkan duplikat tray, dan menghentikan layanan non-kritis opt-in.";
    }

    partial void OnEnableAutoTrayCleanupChanged(bool value)
    {
        if (_isSyncing) return;
        _optimizer.EnableAutoTrayCleanup = value;
        _ = _repository.SetSettingAsync("automation.auto_tray_cleanup", value ? "true" : "false");
        StatusMessage = value
            ? "Pembersihan otomatis ikon tray hantu & duplikat aplikasi (mis. Telegram ganda) diaktifkan."
            : "Pembersihan otomatis System Tray dinonaktifkan.";
    }

    partial void OnEnableAutoStopUnusedServicesChanged(bool value)
    {
        if (_isSyncing) return;
        _optimizer.EnableAutoStopUnusedServices = value;
        _ = _repository.SetSettingAsync("automation.auto_stop_unused_services", value ? "true" : "false");
        StatusMessage = value
            ? "Auto-Stop Layanan Non-Kritis (Opt-In) diaktifkan: hanya layanan kandidat non-kritis yang dihentikan (dengan snapshot rollback otomatis). Layanan inti Windows tetap terkunci."
            : "Auto-Stop Layanan Non-Kritis dinonaktifkan.";
    }

    [RelayCommand]
    private async Task CleanTrayNowAsync()
    {
        var report = await _optimizer.CleanTrayAndDuplicatesAsync();
        StatusMessage = report.Summary;
    }

    [RelayCommand]
    private async Task StopUnusedServicesNowAsync()
    {
        var res = await _optimizer.StopUnusedCandidateServicesAsync(userConfirmed: true);
        StatusMessage = res.Message;
    }

    [RelayCommand]
    public async Task EvaluateNowAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            SyncState();
            var list = await _optimizer.EvaluateRecommendationsAsync();
            Recommendations.Clear();
            foreach (var r in list) Recommendations.Add(r);

            StatusMessage = list.Count == 0
                ? "Evaluasi selesai: tidak ada pelanggaran batas RAM/CPU atau layanan idle yang perlu ditindak."
                : $"Evaluasi selesai: ditemukan {list.Count} rekomendasi optimasi aman.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EmergencyStopAsync()
    {
        await _optimizer.EmergencyStopAsync();
        SyncState();
        StatusMessage = "STOP DARURAT selesai: semua otomatisasi dihentikan dan seluruh proses EcoQoS dikembalikan normal.";
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        await _optimizer.ResumeAutomationAsync();
        SyncState();
        StatusMessage = "Otomatisasi NEXUS kembali aktif.";
    }
}
