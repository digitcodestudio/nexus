using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Contracts;
using Nexus.Core.Formatting;
using Nexus.Models;

namespace Nexus.UI.ViewModels;

public sealed record MemoryConsumerRow(
    int RepresentativePid,
    string GroupKey,
    string DisplayName,
    int ProcessCount,
    string PrivateMemoryText,
    string WorkingSetText,
    string IdleText,
    string StatusText,
    bool IsProtected);

public sealed partial class MemoryOptimizerViewModel : ObservableObject
{
    private readonly ISystemMonitorService _monitor;
    private readonly IProcessManagerService _processManager;
    private readonly IMemoryOptimizer _memoryOptimizer;
    private readonly ICpuOptimizer _cpuOptimizer;

    [ObservableProperty] public partial double RamUsagePercent { get; set; }
    [ObservableProperty] public partial string RamSummaryText { get; set; } = "—";
    [ObservableProperty] public partial string PressureStatusText { get; set; } = "Memeriksa...";
    [ObservableProperty] public partial string GuardrailText { get; set; } = "Batas Target RAM: ≤ 75%";
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public ObservableCollection<MemoryConsumerRow> TopConsumers { get; } = [];
    public ObservableCollection<MemoryLeakSuspect> LeakSuspects { get; } = [];
    public ObservableCollection<OptimizationRecommendation> Recommendations { get; } = [];

    public MemoryOptimizerViewModel(
        ISystemMonitorService monitor,
        IProcessManagerService processManager,
        IMemoryOptimizer memoryOptimizer,
        ICpuOptimizer cpuOptimizer)
    {
        _monitor = monitor;
        _processManager = processManager;
        _memoryOptimizer = memoryOptimizer;
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
            var report = _memoryOptimizer.Analyze(snapshot, groups, _monitor.RamGuardrailPercent);

            RamUsagePercent = report.CurrentUsagePercent;
            RamSummaryText = $"{ByteFormatter.FormatBytes(snapshot.Memory.UsedBytes)} terpakai dari {ByteFormatter.FormatBytes(snapshot.Memory.TotalBytes)} · Tersedia {ByteFormatter.FormatBytes(snapshot.Memory.AvailableBytes)}";
            GuardrailText = $"Batas Target RAM: ≤ {report.GuardrailThresholdPercent:F0}%";
            PressureStatusText = report.IsGuardrailExceeded
                ? $"PERINGATAN: RAM ({report.CurrentUsagePercent:F1}%) melebihi batas {report.GuardrailThresholdPercent:F0}%!"
                : $"Aman — RAM ({report.CurrentUsagePercent:F1}%) di bawah batas {report.GuardrailThresholdPercent:F0}%.";

            TopConsumers.Clear();
            foreach (var g in report.TopConsumers)
            {
                int repPid = g.Processes.OrderByDescending(p => p.PrivateMemoryBytes).First().ProcessId;
                TopConsumers.Add(new MemoryConsumerRow(
                    repPid,
                    g.GroupKey,
                    g.DisplayName,
                    g.ProcessCount,
                    ByteFormatter.FormatBytes(g.TotalPrivateMemoryBytes),
                    ByteFormatter.FormatBytes(g.TotalWorkingSetBytes),
                    g.GroupIdleDuration.TotalMinutes >= 1 ? $"{g.GroupIdleDuration.TotalMinutes:F0} mnt" : "Aktif",
                    g.IsProtected ? "Dilindungi" : g.IsAnyForeground ? "Fokus Aktif" : g.HasActiveBackgroundWork ? "Bekerja" : "Idle",
                    g.IsProtected));
            }

            LeakSuspects.Clear();
            foreach (var l in report.LeakSuspects) LeakSuspects.Add(l);

            Recommendations.Clear();
            foreach (var r in report.Recommendations) Recommendations.Add(r);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyEcoQoSForGroupAsync(MemoryConsumerRow row)
    {
        if (row.IsProtected)
        {
            StatusMessage = $"'{row.DisplayName}' dilindungi dan tidak akan dibatasi.";
            return;
        }

        var groups = await _processManager.GetGroupedApplicationsAsync();
        var match = groups.FirstOrDefault(g => g.GroupKey.Equals(row.GroupKey, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;

        int applied = 0;
        foreach (var proc in match.Processes)
        {
            var res = await _cpuOptimizer.SetEcoQoSAsync(proc.ProcessId, enable: true);
            if (res.Succeeded) applied++;
        }

        StatusMessage = applied > 0
            ? $"Mode Efisiensi (EcoQoS) diterapkan ke {applied} proses '{row.DisplayName}'."
            : $"Tidak dapat menerapkan EcoQoS ke '{row.DisplayName}' (akses sistem/elevasi).";
    }

    public async Task ApplyScopedTrimAsync(MemoryConsumerRow row)
    {
        var res = await _memoryOptimizer.ApplyScopedOptimizationAsync(row.RepresentativePid, userConfirmed: true);
        StatusMessage = res.Message;
        await RefreshAsync();
    }
}

