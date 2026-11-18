using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Models;
using Nexus.Optimizer;

namespace Nexus.UI.ViewModels;

public sealed record ProfileCardModel(
    OptimizationProfile Raw,
    string DisplayName,
    string DescriptionId,
    string GuardrailBadge,
    string PowerBadge,
    bool IsActive);

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly OptimizationEngine _optimizer;

    [ObservableProperty] public partial string ActiveProfileBanner { get; set; } = string.Empty;
    [ObservableProperty] public partial double EditRamThreshold { get; set; } = 75;
    [ObservableProperty] public partial double EditCpuThreshold { get; set; } = 65;
    [ObservableProperty] public partial int EditIdleMinutes { get; set; } = 30;
    [ObservableProperty] public partial bool EditEnableEcoQoS { get; set; } = true;
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;

    public ObservableCollection<ProfileCardModel> Profiles { get; } = [];

    public ProfilesViewModel(OptimizationEngine optimizer)
    {
        _optimizer = optimizer;
    }

    public async Task LoadAsync()
    {
        if (_optimizer.Profiles.Count == 0)
        {
            await _optimizer.InitializeAsync();
        }
        RefreshCards();
    }

    private void RefreshCards()
    {
        var active = _optimizer.ActiveProfile;
        ActiveProfileBanner = $"Profil Aktif Saat Ini: {MainViewModel.TranslateProfileName(active.Type)} (RAM ≤ {active.RamThresholdPercent:F0}% · CPU ≤ {active.CpuThresholdPercent:F0}% · Idle {active.IdleTimeoutMinutes} mnt)";
        EditRamThreshold = active.RamThresholdPercent;
        EditCpuThreshold = active.CpuThresholdPercent;
        EditIdleMinutes = active.IdleTimeoutMinutes;
        EditEnableEcoQoS = active.EnableEcoQoS;

        Profiles.Clear();
        foreach (var p in _optimizer.Profiles)
        {
            Profiles.Add(new ProfileCardModel(
                p,
                MainViewModel.TranslateProfileName(p.Type),
                TranslateDescription(p.Type),
                $"RAM ≤ {p.RamThresholdPercent:F0}% · CPU ≤ {p.CpuThresholdPercent:F0}% · Idle {p.IdleTimeoutMinutes} mnt",
                p.TargetPowerMode switch
                {
                    WindowsPowerMode.BestPowerEfficiency => "Mode Daya: Hemat Daya",
                    WindowsPowerMode.BestPerformance => "Mode Daya: Performa Terbaik",
                    _ => "Mode Daya: Seimbang"
                },
                p.Type == active.Type));
        }
    }

    public async Task ActivateProfileAsync(ProfileType type)
    {
        await _optimizer.SetActiveProfileAsync(type);
        StatusMessage = $"Profil '{MainViewModel.TranslateProfileName(type)}' berhasil diaktifkan.";
        RefreshCards();
    }

    [RelayCommand]
    private async Task SaveActiveProfileEditsAsync()
    {
        var cur = _optimizer.ActiveProfile;
        var updated = cur with
        {
            RamThresholdPercent = Math.Round(EditRamThreshold),
            CpuThresholdPercent = Math.Round(EditCpuThreshold),
            IdleTimeoutMinutes = Math.Clamp(EditIdleMinutes, 5, 180),
            EnableEcoQoS = EditEnableEcoQoS
        };

        await _optimizer.UpdateProfileAsync(updated);
        StatusMessage = $"Perubahan profil '{MainViewModel.TranslateProfileName(updated.Type)}' tersimpan ke SQLite.";
        RefreshCards();
    }

    private static string TranslateDescription(ProfileType type) => type switch
    {
        ProfileType.Balanced => "Perilaku Windows normal dengan optimasi latar belakang aman. Menjaga RAM ≤ 75% dan CPU ≤ 65%.",
        ProfileType.Development => "Melindungi Antigravity, VS Code, browser aktif, database, terminal, dan proses build agar tidak pernah terganggu.",
        ProfileType.HeavyWorkload => "Memprioritaskan performa CPU & ketersediaan RAM penuh saat beban kerja berat tanpa interupsi latar belakang.",
        ProfileType.PowerSaving => "Mengaktifkan Mode Efisiensi (EcoQoS) lebih agresif untuk aplikasi idle dan mode hemat daya Windows.",
        _ => string.Empty
    };
}

