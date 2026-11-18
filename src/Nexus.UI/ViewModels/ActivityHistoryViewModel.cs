using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Contracts;
using Nexus.Models;

namespace Nexus.UI.ViewModels;

public sealed record ActivityRow(
    string TimeLocal,
    string SeverityText,
    string Module,
    string Target,
    string Summary);

public sealed record BackupRow(
    Guid Id,
    string ServiceName,
    string OriginalConfigText,
    string CapturedLocal,
    string StatusText,
    bool CanRollback);

public sealed partial class ActivityHistoryViewModel : ObservableObject
{
    private readonly INexusRepository _repository;
    private readonly IWindowsServiceManager _serviceManager;

    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public ObservableCollection<ActivityRow> Activities { get; } = [];
    public ObservableCollection<BackupRow> ServiceBackups { get; } = [];

    public ActivityHistoryViewModel(INexusRepository repository, IWindowsServiceManager serviceManager)
    {
        _repository = repository;
        _serviceManager = serviceManager;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var logs = await _repository.GetActivityHistoryAsync(200);
            Activities.Clear();
            foreach (var l in logs)
            {
                Activities.Add(new ActivityRow(
                    l.TimestampUtc.ToLocalTime().ToString("dd/MM HH:mm:ss"),
                    l.Severity switch
                    {
                        ActivitySeverity.Success => "Sukses",
                        ActivitySeverity.Warning => "Peringatan",
                        ActivitySeverity.Error => "Galat",
                        _ => "Info"
                    },
                    l.Module,
                    l.Target,
                    l.Summary));
            }

            var backups = await _serviceManager.GetBackupsAsync();
            ServiceBackups.Clear();
            foreach (var b in backups)
            {
                ServiceBackups.Add(new BackupRow(
                    b.Id,
                    b.ServiceName,
                    $"Startup Awal: {b.OriginalStartupType} · Status Awal: {b.OriginalState}",
                    b.CapturedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    b.RolledBackUtc.HasValue ? "Sudah Dikembalikan" : "Tersedia untuk Rollback",
                    !b.RolledBackUtc.HasValue));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RollbackAsync(BackupRow row)
    {
        var res = await _serviceManager.RollbackServiceAsync(row.ServiceName);
        StatusMessage = res.Message;
        await RefreshAsync();
    }
}

