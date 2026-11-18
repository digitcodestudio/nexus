using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Contracts;
using Nexus.CpuOptimizer;
using Nexus.Models;

namespace Nexus.UI.ViewModels;

public sealed record CpuConsumerRow(
    int RepresentativePid,
    string GroupKey,
    string DisplayName,
    int ProcessCount,
    string CpuText,
    string StatusText,
    bool IsEcoQoSActive);

public sealed partial class CpuOptimizerViewModel : ObservableObject
{
    private readonly ISystemMonitorService _monitor;
    private readonly IProcessManagerService _processManager;
    private readonly ICpuOptimizer _cpuOptimizer;

    [ObservableProperty] public partial double CpuUsagePercent { get; set; }
    [ObservableProperty] public partial string CpuSummaryText { get; set; } = "—";
    [ObservableProperty] public partial string GuardrailText { get; set; } = "Batas Target CPU: ≤ 65%";
    [ObservableProperty] public partial string EcoQoSCountText { get; set; } = "0 proses dalam Mode Efisiensi (EcoQoS)";
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public ObservableCollection<CpuConsumerRow> BackgroundConsumers { get; } = [];
    public ObservableCollection<OptimizationRecommendation> Recommendations { get; } = [];

    public CpuOptimizerViewModel(
        ISystemMonitorService monitor,
        IProcessManagerService processManager,
        ICpuOptimizer cpuOptimizer)
    {
        _monitor = monitor;
        _processManager = processManager;
        _cpuOptimizer = cpuOptimizer;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var snapshot = _monitor.LatestSnapshot;
            if (snapshot is null) return;

            var groups = await _processManager.GetGroupedApplicationsAsync();
            var report = _cpuOptimizer.Analyze(snapshot, groups, _monitor.CpuGuardrailPercent);

            CpuUsagePercent = report.CurrentUsagePercent;
            GuardrailText = $"Batas Target CPU: ≤ {report.GuardrailThresholdPercent:F0}%";
            CpuSummaryText = report.IsSustainedHighLoad
                ? $"PERINGATAN: Beban CPU berkelanjutan ({report.CurrentUsagePercent:F1}%) melebihi batas {report.GuardrailThresholdPercent:F0}%!"
                : $"CPU saat ini {report.CurrentUsagePercent:F1}% · Frekuensi {(snapshot.Cpu.CurrentFrequencyMhz.HasValue ? $"{snapshot.Cpu.CurrentFrequencyMhz.Value / 1000.0:F2} GHz" : "—")}";

            if (_cpuOptimizer is CpuOptimizerService svc)
            {
                EcoQoSCountText = $"{svc.ActiveEcoQoSCount} proses aktif dalam Mode Efisiensi (EcoQoS)";
            }

            BackgroundConsumers.Clear();
            foreach (var g in report.BackgroundLoadConsumers)
            {
                int repPid = g.Processes.OrderByDescending(p => p.CpuPercent).First().ProcessId;
                bool anyEco = g.Processes.Any(p => p.IsEcoQoSActive);
                BackgroundConsumers.Add(new CpuConsumerRow(
                    repPid,
                    g.GroupKey,
                    g.DisplayName,
                    g.ProcessCount,
                    $"{g.TotalCpuPercent:F1}%",
                    anyEco ? "EcoQoS Aktif" : "Latar Belakang",
                    anyEco));
            }

            Recommendations.Clear();
            foreach (var r in report.Recommendations) Recommendations.Add(r);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetPowerModeAsync(string modeKey)
    {
        if (!Enum.TryParse<WindowsPowerMode>(modeKey, out var mode)) return;
        var res = await _cpuOptimizer.SetPowerModeAsync(mode);
        StatusMessage = res.Message;
    }

    [RelayCommand]
    private async Task RevertAllEcoQoSAsync()
    {
        if (_cpuOptimizer is CpuOptimizerService svc)
        {
            await svc.RevertAllEcoQoSAsync();
            StatusMessage = "Seluruh proses Mode Efisiensi (EcoQoS) dikembalikan ke prioritas normal.";
            await RefreshAsync();
        }
    }

    public async Task ToggleEcoQoSForGroupAsync(CpuConsumerRow row, bool enable)
    {
        var groups = await _processManager.GetGroupedApplicationsAsync();
        var match = groups.FirstOrDefault(g => g.GroupKey.Equals(row.GroupKey, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;

        int changed = 0;
        foreach (var p in match.Processes)
        {
            var res = await _cpuOptimizer.SetEcoQoSAsync(p.ProcessId, enable);
            if (res.Succeeded) changed++;
        }

        StatusMessage = enable
            ? $"Mode Efisiensi (EcoQoS) diaktifkan untuk {changed} proses '{row.DisplayName}'."
            : $"Mode Efisiensi (EcoQoS) dinonaktifkan untuk {changed} proses '{row.DisplayName}'.";
        await RefreshAsync();
    }
}

