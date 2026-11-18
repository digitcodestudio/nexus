using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Nexus.Core.Contracts;
using Nexus.Core.Formatting;
using Nexus.Models;

namespace Nexus.UI.ViewModels;

public sealed record CoreLoadItem(string CoreLabel, double Percent, string PercentText);

public sealed partial class PerformanceViewModel : ObservableObject, IDisposable
{
    private readonly ISystemMonitorService _monitor;
    private DispatcherQueue? _dispatcher;

    [ObservableProperty] public partial string CpuModelText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CpuLoadSummary { get; set; } = "0.0%";
    [ObservableProperty] public partial string CpuFreqText { get; set; } = "—";
    [ObservableProperty] public partial string CpuSustainedStatus { get; set; } = "Normal";

    [ObservableProperty] public partial string RamUsedText { get; set; } = "—";
    [ObservableProperty] public partial string RamAvailableText { get; set; } = "—";
    [ObservableProperty] public partial string RamCachedText { get; set; } = "—";
    [ObservableProperty] public partial string RamCommittedText { get; set; } = "—";
    [ObservableProperty] public partial string RamPressureText { get; set; } = "Normal";
    [ObservableProperty] public partial double RamUsagePercent { get; set; }

    [ObservableProperty] public partial string DiskActiveText { get; set; } = "0%";
    [ObservableProperty] public partial string DiskReadText { get; set; } = "0 B/s";
    [ObservableProperty] public partial string DiskWriteText { get; set; } = "0 B/s";
    [ObservableProperty] public partial string DiskCapacityText { get; set; } = "—";

    [ObservableProperty] public partial string NetAdapterText { get; set; } = "—";
    [ObservableProperty] public partial string NetDownText { get; set; } = "0 B/s";
    [ObservableProperty] public partial string NetUpText { get; set; } = "0 B/s";
    [ObservableProperty] public partial string OsHardwareText { get; set; } = "—";

    public ObservableCollection<CoreLoadItem> CoreLoads { get; } = [];

    public PerformanceViewModel(ISystemMonitorService monitor)
    {
        _monitor = monitor;
        var hw = monitor.HardwareInfo;
        CpuModelText = hw.CpuName;
        OsHardwareText = $"{hw.WindowsEdition} ({hw.WindowsVersion} · Build {hw.OsBuildNumber} · {hw.Architecture})";
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    public void AttachDispatcher(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        if (_monitor.LatestSnapshot is not null) ApplySnapshot(_monitor.LatestSnapshot);
    }

    private void OnSnapshotUpdated(object? sender, SystemSnapshot s)
        => _dispatcher?.TryEnqueue(() => ApplySnapshot(s));

    private void ApplySnapshot(SystemSnapshot s)
    {
        CpuLoadSummary = $"{s.Cpu.TotalUsagePercent:F1}%";
        CpuFreqText = s.Cpu.CurrentFrequencyMhz.HasValue ? $"{s.Cpu.CurrentFrequencyMhz.Value / 1000.0:F2} GHz" : "Tidak tersedia";
        CpuSustainedStatus = s.Cpu.IsSustainedHighLoad
            ? $"Beban Tinggi Berkelanjutan (> {_monitor.CpuGuardrailPercent:F0}%)"
            : s.Cpu.IsAboveGuardrail ? "Lonjakan Sementara" : "Stabil di bawah batas";

        RamUsagePercent = s.Memory.UsagePercent;
        RamUsedText = $"{ByteFormatter.FormatBytes(s.Memory.UsedBytes)} / {ByteFormatter.FormatBytes(s.Memory.TotalBytes)} ({s.Memory.UsagePercent:F1}%)";
        RamAvailableText = ByteFormatter.FormatBytes(s.Memory.AvailableBytes);
        RamCachedText = ByteFormatter.FormatBytes(s.Memory.CachedBytes);
        RamCommittedText = $"{ByteFormatter.FormatBytes(s.Memory.CommittedBytes)} / {ByteFormatter.FormatBytes(s.Memory.CommitLimitBytes)}";
        RamPressureText = s.Memory.PressureLevel switch
        {
            MemoryPressureLevel.Critical => "Kritis (≥ 88%)",
            MemoryPressureLevel.Pressure => $"Tekanan Tinggi (≥ {_monitor.RamGuardrailPercent:F0}%)",
            MemoryPressureLevel.Elevated => "Meningkat",
            _ => "Normal"
        };

        DiskActiveText = $"{s.Disk.ActiveTimePercent:F0}%";
        DiskReadText = ByteFormatter.FormatBytesPerSec(s.Disk.ReadBytesPerSec);
        DiskWriteText = ByteFormatter.FormatBytesPerSec(s.Disk.WriteBytesPerSec);
        DiskCapacityText = $"{s.Disk.SystemDriveLetter} {ByteFormatter.FormatBytes(s.Disk.SystemDriveUsedBytes)} / {ByteFormatter.FormatBytes(s.Disk.SystemDriveTotalBytes)}";

        NetAdapterText = s.Network.ActiveAdapterName;
        NetDownText = ByteFormatter.FormatBytesPerSec(s.Network.DownloadBytesPerSec);
        NetUpText = ByteFormatter.FormatBytesPerSec(s.Network.UploadBytesPerSec);

        CoreLoads.Clear();
        for (int i = 0; i < s.Cpu.PerCoreUsagePercent.Length; i++)
        {
            double val = s.Cpu.PerCoreUsagePercent[i];
            CoreLoads.Add(new CoreLoadItem($"Core {i + 1}", val, $"{val:F0}%"));
        }
    }

    public void Dispose() => _monitor.SnapshotUpdated -= OnSnapshotUpdated;
}

