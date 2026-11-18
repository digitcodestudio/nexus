using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Contracts;
using Nexus.Core.Formatting;
using Nexus.Models;

namespace Nexus.UI.ViewModels;

public sealed record ProcessDisplayRow(
    int ProcessId,
    string ProcessName,
    string DisplayName,
    string InstanceText,
    string CpuText,
    string MemoryText,
    string IdleText,
    string ActivityText,
    bool IsProtected,
    bool IsImmutable,
    string ExecutablePath);

public sealed partial class ProcessManagerViewModel : ObservableObject
{
    private readonly IProcessManagerService _processManager;
    private IReadOnlyList<ApplicationProcessGroup> _cachedGroups = [];
    private IReadOnlyList<ProcessInfo> _cachedFlat = [];

    [ObservableProperty] public partial string SearchQuery { get; set; } = string.Empty;
    [ObservableProperty] public partial bool GroupByApplication { get; set; } = true;
    [ObservableProperty] public partial bool IdleOnlyFilter { get; set; }
    [ObservableProperty] public partial int IdleThresholdMinutes { get; set; } = 30;
    [ObservableProperty] public partial string SummaryText { get; set; } = "Memuat daftar proses...";
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial ProcessDisplayRow? SelectedRow { get; set; }

    public ObservableCollection<ProcessDisplayRow> Rows { get; } = [];

    public ProcessManagerViewModel(IProcessManagerService processManager)
    {
        _processManager = processManager;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _cachedFlat = await _processManager.GetRunningProcessesAsync();
            _cachedGroups = ProcessManager.ProcessManagerService.GroupProcesses(_cachedFlat);
            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnGroupByApplicationChanged(bool value) => ApplyFilter();
    partial void OnIdleOnlyFilterChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        Rows.Clear();
        string q = SearchQuery.Trim();
        var idleLimit = TimeSpan.FromMinutes(Math.Max(1, IdleThresholdMinutes));

        if (GroupByApplication)
        {
            var filtered = _cachedGroups.Where(g =>
                (string.IsNullOrEmpty(q) ||
                 g.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                 g.GroupKey.Contains(q, StringComparison.OrdinalIgnoreCase)) &&
                (!IdleOnlyFilter || (!g.IsProtected && !g.IsAnyForeground && !g.HasActiveBackgroundWork && g.GroupIdleDuration >= idleLimit)));

            foreach (var g in filtered)
            {
                var rep = g.Processes.FirstOrDefault(p => p.HasVisibleWindow) ?? g.Processes[0];
                Rows.Add(new ProcessDisplayRow(
                    rep.ProcessId,
                    g.GroupKey,
                    g.DisplayName,
                    g.ProcessCount > 1 ? $"{g.ProcessCount} proses" : $"PID {rep.ProcessId}",
                    $"{g.TotalCpuPercent:F1}%",
                    ByteFormatter.FormatBytes(g.TotalPrivateMemoryBytes),
                    g.GroupIdleDuration.TotalMinutes >= 1 ? $"{g.GroupIdleDuration.TotalMinutes:F0} mnt" : "Aktif",
                    g.IsAnyForeground ? "Fokus Utama" : g.HasActiveBackgroundWork ? "Bekerja (CPU/IO)" : g.IsProtected ? "Dilindungi" : "Idle",
                    g.IsProtected,
                    g.Processes.Any(p => p.IsImmutableSystem),
                    rep.ExecutablePath ?? "—"));
            }
        }
        else
        {
            var filtered = _cachedFlat.Where(p =>
                (string.IsNullOrEmpty(q) ||
                 p.ApplicationName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                 p.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                 p.ProcessId.ToString().Contains(q)) &&
                (!IdleOnlyFilter || (!p.IsProtected && p.ActivityStatus == ProcessActivityStatus.Idle && p.IdleDuration >= idleLimit)));

            foreach (var p in filtered)
            {
                Rows.Add(new ProcessDisplayRow(
                    p.ProcessId,
                    p.ProcessName,
                    p.ApplicationName,
                    $"PID {p.ProcessId}",
                    $"{p.CpuPercent:F1}%",
                    ByteFormatter.FormatBytes(p.PrivateMemoryBytes),
                    p.IdleDuration.TotalMinutes >= 1 ? $"{p.IdleDuration.TotalMinutes:F0} mnt" : "Aktif",
                    TranslateActivity(p.ActivityStatus),
                    p.IsProtected,
                    p.IsImmutableSystem,
                    p.ExecutablePath ?? "—"));
            }
        }

        SummaryText = $"Menampilkan {Rows.Count} item dari {_cachedFlat.Count} total proses aktif ({_cachedGroups.Count} grup aplikasi).";
    }

    public async Task ToggleProtectionAsync(ProcessDisplayRow row)
    {
        if (row.IsImmutable)
        {
            StatusMessage = $"'{row.DisplayName}' adalah proses inti Windows yang terkunci permanen.";
            return;
        }

        await _processManager.SetProtectionAsync(row.ProcessName, !row.IsProtected);
        StatusMessage = !row.IsProtected
            ? $"'{row.DisplayName}' sekarang DILINDUNGI di whitelist."
            : $"'{row.DisplayName}' dihapus dari whitelist perlindungan.";
        await RefreshAsync();
    }

    public async Task ExecuteActionAsync(ProcessDisplayRow row, SafeActionType action, bool confirmed)
    {
        var res = await _processManager.ExecuteSafeActionAsync(row.ProcessId, action, confirmed);
        StatusMessage = res.Message;
        await RefreshAsync();
    }

    private static string TranslateActivity(ProcessActivityStatus s) => s switch
    {
        ProcessActivityStatus.ActiveForeground => "Fokus Utama",
        ProcessActivityStatus.ActiveBackgroundWork => "Bekerja (CPU/IO)",
        ProcessActivityStatus.Idle => "Idle",
        ProcessActivityStatus.Protected => "Dilindungi",
        ProcessActivityStatus.System => "Sistem Windows",
        _ => "—"
    };
}

