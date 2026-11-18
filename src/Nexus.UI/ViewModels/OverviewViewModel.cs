using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Nexus.Core.Contracts;
using Nexus.Core.Formatting;
using Nexus.Models;
using Nexus.Optimizer;
using SkiaSharp;
using Windows.UI;

namespace Nexus.UI.ViewModels;

public sealed record TopProcessRow(
    string DisplayName,
    string ProcessName,
    string InstanceBadge,
    string MemoryFormatted,
    string CpuFormatted,
    string ProtectionBadge);

public sealed partial class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly ISystemMonitorService _monitor;
    private readonly OptimizationEngine _optimizer;
    private DispatcherQueue? _dispatcher;
    private TimeSpan _selectedWindow = TimeSpan.FromMinutes(1);

    private readonly ObservableCollection<ObservableValue> _cpuPoints = [];
    private readonly ObservableCollection<ObservableValue> _ramPoints = [];
    private readonly ObservableCollection<ObservableValue> _netDownPoints = [];
    private readonly ObservableCollection<ObservableValue> _diskActivePoints = [];
    private readonly ObservableCollection<ObservableValue> _coreValues = [];

    private static readonly SolidColorBrush AccentBlueBrush = new(Color.FromArgb(255, 76, 194, 255));
    private static readonly SolidColorBrush AccentTealBrush = new(Color.FromArgb(255, 45, 212, 191));
    private static readonly SolidColorBrush AccentPurpleBrush = new(Color.FromArgb(255, 167, 139, 250));
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromArgb(255, 63, 185, 80));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromArgb(255, 210, 153, 34));
    private static readonly SolidColorBrush CriticalBrush = new(Color.FromArgb(255, 248, 81, 73));

    [ObservableProperty] public partial string HardwareSummaryLine { get; set; } = "Mendeteksi perangkat keras...";
    [ObservableProperty] public partial string UptimeText { get; set; } = "Waktu Aktif: —";
    [ObservableProperty] public partial string SelectedZoomLabel { get; set; } = "1m";
    [ObservableProperty] public partial string ProfileBadgeText { get; set; } = "Profil: Seimbang · Mode Aman";

    // KPI Card 1: CPU
    [ObservableProperty] public partial string CpuPrimaryText { get; set; } = "0.0%";
    [ObservableProperty] public partial string CpuSecondaryText { get; set; } = "Mengukur frekuensi...";
    [ObservableProperty] public partial string CpuBadgeText { get; set; } = "Batas ≤ 65%";
    [ObservableProperty] public partial double CpuPercent { get; set; }
    [ObservableProperty] public partial Brush CpuAccentBrush { get; set; } = AccentBlueBrush;

    // KPI Card 2: RAM
    [ObservableProperty] public partial string RamPrimaryText { get; set; } = "0.00 / 0.00 GB";
    [ObservableProperty] public partial string RamSecondaryText { get; set; } = "Tersedia: —";
    [ObservableProperty] public partial string RamBadgeText { get; set; } = "0.0%";
    [ObservableProperty] public partial double RamPercent { get; set; }
    [ObservableProperty] public partial Brush RamAccentBrush { get; set; } = AccentTealBrush;

    // KPI Card 3: Committed / Cache
    [ObservableProperty] public partial string CommitPrimaryText { get; set; } = "0.00 / 0.00 GB";
    [ObservableProperty] public partial string CommitSecondaryText { get; set; } = "Cache Sistem: —";
    [ObservableProperty] public partial string CommitBadgeText { get; set; } = "0%";
    [ObservableProperty] public partial double CommitPercent { get; set; }
    [ObservableProperty] public partial Brush CommitAccentBrush { get; set; } = AccentPurpleBrush;

    // KPI Card 4: Disk & Network
    [ObservableProperty] public partial string DiskPrimaryText { get; set; } = "0.0 / 0.0 GB";
    [ObservableProperty] public partial string DiskSecondaryText { get; set; } = "Jaringan: ↓ 0 B/s · ↑ 0 B/s";
    [ObservableProperty] public partial string DiskBadgeText { get; set; } = "C:";
    [ObservableProperty] public partial double DiskUsagePercent { get; set; }
    [ObservableProperty] public partial Brush DiskAccentBrush { get; set; } = AccentBlueBrush;

    // Health Summary
    [ObservableProperty] public partial string HealthHeadline { get; set; } = "Sistem Sehat — Dalam Batas Aman";
    [ObservableProperty] public partial string HealthDetail { get; set; } = "Target batas RAM ≤ 75% dan CPU ≤ 65% aktif (Mode Aman).";
    [ObservableProperty] public partial Brush HealthBadgeBrush { get; set; } = SuccessBrush;
    [ObservableProperty] public partial string ProcessServiceSummary { get; set; } = "— proses · — layanan berjalan";
    [ObservableProperty] public partial string NetworkSummaryText { get; set; } = "↓ 0 B/s  ↑ 0 B/s";
    [ObservableProperty] public partial string DiskThroughputText { get; set; } = "Baca 0 B/s · Tulis 0 B/s";

    public ObservableCollection<TopProcessRow> TopProcesses { get; } = [];

    private readonly RectangularSection _cpuThresholdSection;
    private readonly RectangularSection _ramThresholdSection;

    public ISeries[] CpuSeries { get; }
    public ISeries[] CoreBarSeries { get; }
    public ISeries[] RamSeries { get; }
    public ISeries[] ActivitySeries { get; }

    public IEnumerable<ICartesianAxis> CpuYAxes { get; }
    public IEnumerable<ICartesianAxis> HiddenXAxes { get; }
    public IEnumerable<ICartesianAxis> CoreXAxes { get; }
    public IEnumerable<ICartesianAxis> CoreYAxes { get; }
    public IEnumerable<ICartesianAxis> RamYAxes { get; }
    public IEnumerable<ICartesianAxis> ActivityYAxes { get; }

    public IEnumerable<IChartElement> CpuThresholdSections { get; }
    public IEnumerable<IChartElement> RamThresholdSections { get; }

    public OverviewViewModel(ISystemMonitorService monitor, OptimizationEngine optimizer)
    {
        _monitor = monitor;
        _optimizer = optimizer;
        var hw = monitor.HardwareInfo;
        HardwareSummaryLine =
            $"CPU: {hw.CpuName}  |  Thread: {hw.LogicalProcessors}  |  RAM: {ByteFormatter.FormatBytes(hw.TotalPhysicalMemoryBytes)}  |  OS: {hw.WindowsEdition} ({hw.WindowsVersion} · Build {hw.OsBuildNumber})";

        var coreLabels = new string[hw.LogicalProcessors];
        for (int i = 0; i < hw.LogicalProcessors; i++)
        {
            _coreValues.Add(new ObservableValue(0));
            coreLabels[i] = $"C{i + 1}";
        }

        CpuSeries =
        [
            new LineSeries<ObservableValue>
            {
                Name = "CPU %",
                Values = _cpuPoints,
                GeometrySize = 0,
                LineSmoothness = 0.35,
                Stroke = new SolidColorPaint(new SKColor(76, 194, 255), 2),
                Fill = new LinearGradientPaint(
                    [new SKColor(76, 194, 255, 72), new SKColor(76, 194, 255, 4)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1))
            }
        ];

        CoreBarSeries =
        [
            new ColumnSeries<ObservableValue>
            {
                Name = "Beban Core %",
                Values = _coreValues,
                Fill = new SolidColorPaint(new SKColor(112, 92, 240, 225)),
                MaxBarWidth = 14,
                Rx = 3,
                Ry = 3
            }
        ];

        RamSeries =
        [
            new LineSeries<ObservableValue>
            {
                Name = "RAM %",
                Values = _ramPoints,
                GeometrySize = 0,
                LineSmoothness = 0.25,
                Stroke = new SolidColorPaint(new SKColor(45, 212, 191), 2),
                Fill = new LinearGradientPaint(
                    [new SKColor(45, 212, 191, 68), new SKColor(45, 212, 191, 4)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1))
            }
        ];

        ActivitySeries =
        [
            new LineSeries<ObservableValue>
            {
                Name = "Disk Aktif %",
                Values = _diskActivePoints,
                GeometrySize = 0,
                LineSmoothness = 0.3,
                Stroke = new SolidColorPaint(new SKColor(167, 139, 250), 2),
                Fill = new LinearGradientPaint(
                    [new SKColor(167, 139, 250, 56), new SKColor(167, 139, 250, 4)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1))
            },
            new LineSeries<ObservableValue>
            {
                Name = "Unduh (MB/s)",
                Values = _netDownPoints,
                GeometrySize = 0,
                LineSmoothness = 0.3,
                Stroke = new SolidColorPaint(new SKColor(56, 189, 248), 2),
                Fill = null
            }
        ];

        var gridPaint = new SolidColorPaint(new SKColor(37, 45, 59, 150), 1);
        var labelPaint = new SolidColorPaint(new SKColor(148, 163, 184, 210));

        HiddenXAxes = [new Axis { IsVisible = false, ShowSeparatorLines = false }];
        CpuYAxes = [new Axis { MinLimit = 0, MaxLimit = 100, MinStep = 25, Labeler = v => $"{v:F0}%", LabelsPaint = labelPaint, SeparatorsPaint = gridPaint, TextSize = 11 }];
        RamYAxes = [new Axis { MinLimit = 0, MaxLimit = 100, MinStep = 25, Labeler = v => $"{v:F0}%", LabelsPaint = labelPaint, SeparatorsPaint = gridPaint, TextSize = 11 }];
        ActivityYAxes = [new Axis { MinLimit = 0, Labeler = v => $"{v:F0}", LabelsPaint = labelPaint, SeparatorsPaint = gridPaint, TextSize = 11 }];
        CoreXAxes = [new Axis { Labels = coreLabels, LabelsPaint = labelPaint, TextSize = 10, ShowSeparatorLines = false, MinStep = 1 }];
        CoreYAxes = [new Axis { MinLimit = 0, MaxLimit = 100, MinStep = 25, Labeler = v => $"{v:F0}%", LabelsPaint = labelPaint, SeparatorsPaint = gridPaint, TextSize = 10 }];

        _cpuThresholdSection = new RectangularSection
        {
            Yi = _monitor.CpuGuardrailPercent,
            Yj = _monitor.CpuGuardrailPercent,
            Stroke = new SolidColorPaint(new SKColor(210, 153, 34, 200), 1.5f) { PathEffect = new DashEffect([5, 4]) }
        };
        CpuThresholdSections = [_cpuThresholdSection];

        _ramThresholdSection = new RectangularSection
        {
            Yi = _monitor.RamGuardrailPercent,
            Yj = _monitor.RamGuardrailPercent,
            Stroke = new SolidColorPaint(new SKColor(248, 81, 73, 200), 1.5f) { PathEffect = new DashEffect([5, 4]) }
        };
        RamThresholdSections = [_ramThresholdSection];

        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    public void AttachDispatcher(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        if (_cpuPoints.Count == 0)
        {
            RebuildSeriesFromHistory();
        }
        if (_monitor.LatestSnapshot is not null)
        {
            ApplySnapshot(_monitor.LatestSnapshot, appendToSeries: false);
        }
    }

    [RelayCommand]
    private void SetZoomWindow(string windowKey)
    {
        SelectedZoomLabel = windowKey;
        _selectedWindow = windowKey switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "5m" => TimeSpan.FromMinutes(5),
            "30m" => TimeSpan.FromMinutes(30),
            _ => TimeSpan.MaxValue
        };

        RebuildSeriesFromHistory();
    }

    private void RebuildSeriesFromHistory()
    {
        var history = _monitor.GetHistory(_selectedWindow);
        int maxPoints = GetMaxPointsForWindow();

        _cpuPoints.Clear();
        _ramPoints.Clear();
        _diskActivePoints.Clear();
        _netDownPoints.Clear();

        int step = Math.Max(1, history.Count / Math.Max(1, maxPoints));
        for (int i = 0; i < history.Count; i += step)
        {
            var s = history[i];
            _cpuPoints.Add(new ObservableValue(s.Cpu.TotalUsagePercent));
            _ramPoints.Add(new ObservableValue(s.Memory.UsagePercent));
            _diskActivePoints.Add(new ObservableValue(s.Disk.ActiveTimePercent));
            _netDownPoints.Add(new ObservableValue(Math.Round(ByteFormatter.ToMegabytes(s.Network.DownloadBytesPerSec), 2)));
        }
    }

    private int GetMaxPointsForWindow() => SelectedZoomLabel switch
    {
        "1m" => 60,
        "5m" => 120,
        "30m" => 180,
        _ => 240
    };

    private void OnSnapshotUpdated(object? sender, SystemSnapshot snapshot)
    {
        _dispatcher?.TryEnqueue(() => ApplySnapshot(snapshot, appendToSeries: true));
    }

    private void ApplySnapshot(SystemSnapshot s, bool appendToSeries)
    {
        _cpuThresholdSection.Yi = _monitor.CpuGuardrailPercent;
        _cpuThresholdSection.Yj = _monitor.CpuGuardrailPercent;
        _ramThresholdSection.Yi = _monitor.RamGuardrailPercent;
        _ramThresholdSection.Yj = _monitor.RamGuardrailPercent;

        ProfileBadgeText = $"Profil: {MainViewModel.TranslateProfileName(_optimizer.ActiveProfile.Type)} · {(_optimizer.IsSafeModeEnabled ? "Mode Aman" : "Otomatisasi Aktif")}";
        UptimeText = $"Waktu Aktif Sistem: {ByteFormatter.FormatUptime(s.SystemUptime)}";
        ProcessServiceSummary = $"{s.RunningProcessCount} proses aktif · {s.RunningServiceCount} layanan Windows berjalan";

        // CPU KPI
        CpuPercent = s.Cpu.TotalUsagePercent;
        CpuPrimaryText = $"{s.Cpu.TotalUsagePercent:F1}%";
        CpuSecondaryText = s.Cpu.CurrentFrequencyMhz.HasValue
            ? $"{s.Cpu.CurrentFrequencyMhz.Value / 1000.0:F2} GHz · {_monitor.HardwareInfo.LogicalProcessors} thread"
            : $"{_monitor.HardwareInfo.LogicalProcessors} thread logis";
        CpuBadgeText = s.Cpu.IsAboveGuardrail ? $"TINGGI > {_monitor.CpuGuardrailPercent:F0}%" : $"Target ≤ {_monitor.CpuGuardrailPercent:F0}%";
        CpuAccentBrush = s.Cpu.IsAboveGuardrail
            ? CriticalBrush
            : (s.Cpu.TotalUsagePercent >= _monitor.CpuGuardrailPercent - 10 ? WarningBrush : AccentBlueBrush);

        // RAM KPI
        RamPercent = s.Memory.UsagePercent;
        RamPrimaryText = $"{ByteFormatter.ToGigabytes(s.Memory.UsedBytes):F2} / {ByteFormatter.ToGigabytes(s.Memory.TotalBytes):F2} GB";
        RamSecondaryText = $"Tersedia: {ByteFormatter.ToGigabytes(s.Memory.AvailableBytes):F2} GB · Batas ≤ {_monitor.RamGuardrailPercent:F0}%";
        RamBadgeText = $"{s.Memory.UsagePercent:F1}%";
        RamAccentBrush = s.Memory.PressureLevel switch
        {
            MemoryPressureLevel.Critical or MemoryPressureLevel.Pressure => CriticalBrush,
            MemoryPressureLevel.Elevated => WarningBrush,
            _ => AccentTealBrush
        };

        // Committed KPI
        double commitPct = s.Memory.CommitLimitBytes > 0
            ? Math.Clamp((double)s.Memory.CommittedBytes / s.Memory.CommitLimitBytes * 100.0, 0.0, 100.0)
            : 0;
        CommitPercent = commitPct;
        CommitPrimaryText = $"{ByteFormatter.ToGigabytes(s.Memory.CommittedBytes):F2} / {ByteFormatter.ToGigabytes(s.Memory.CommitLimitBytes):F2} GB";
        CommitSecondaryText = $"Cache Sistem: {ByteFormatter.ToGigabytes(s.Memory.CachedBytes):F2} GB";
        CommitBadgeText = $"{commitPct:F0}%";

        // Disk & Network KPI
        double diskPct = s.Disk.SystemDriveTotalBytes > 0
            ? Math.Clamp((double)s.Disk.SystemDriveUsedBytes / s.Disk.SystemDriveTotalBytes * 100.0, 0.0, 100.0)
            : 0;
        DiskUsagePercent = diskPct;
        DiskPrimaryText = $"{ByteFormatter.ToGigabytes(s.Disk.SystemDriveUsedBytes):F1} / {ByteFormatter.ToGigabytes(s.Disk.SystemDriveTotalBytes):F1} GB";
        DiskSecondaryText = $"Aktif {s.Disk.ActiveTimePercent:F0}% · ↓ {ByteFormatter.FormatBytesPerSec(s.Network.DownloadBytesPerSec)}";
        DiskBadgeText = $"{s.Disk.SystemDriveLetter} {diskPct:F0}%";

        NetworkSummaryText = $"{s.Network.ActiveAdapterName}:  ↓ {ByteFormatter.FormatBytesPerSec(s.Network.DownloadBytesPerSec)}   ↑ {ByteFormatter.FormatBytesPerSec(s.Network.UploadBytesPerSec)}";
        DiskThroughputText = $"Disk Aktif {s.Disk.ActiveTimePercent:F0}%   ·   Baca {ByteFormatter.FormatBytesPerSec(s.Disk.ReadBytesPerSec)}   ·   Tulis {ByteFormatter.FormatBytesPerSec(s.Disk.WriteBytesPerSec)}";

        // Health Banner
        if (s.Memory.IsAboveGuardrail || s.Cpu.IsSustainedHighLoad)
        {
            HealthHeadline = "Batas Sumber Daya Terlampaui — Perlu Optimasi";
            HealthDetail = s.Memory.IsAboveGuardrail && s.Cpu.IsSustainedHighLoad
                ? $"RAM ({s.Memory.UsagePercent:F1}% > {_monitor.RamGuardrailPercent:F0}%) dan beban CPU berkelanjutan ({s.Cpu.TotalUsagePercent:F1}% > {_monitor.CpuGuardrailPercent:F0}%) melewati batas target."
                : s.Memory.IsAboveGuardrail
                    ? $"Penggunaan RAM ({s.Memory.UsagePercent:F1}%) melebihi batas {_monitor.RamGuardrailPercent:F0}%. Periksa grup browser (Chrome) & aplikasi idle di tab Optimasi RAM."
                    : $"Beban CPU berkelanjutan ({s.Cpu.TotalUsagePercent:F1}%) melebihi batas {_monitor.CpuGuardrailPercent:F0}%. Periksa aplikasi latar belakang di tab Optimasi CPU.";
            HealthBadgeBrush = CriticalBrush;
        }
        else if (s.Memory.PressureLevel == MemoryPressureLevel.Elevated || s.Cpu.IsAboveGuardrail)
        {
            HealthHeadline = "Mendekati Batas Target Sumber Daya";
            HealthDetail = $"Memantau beban latar belakang (RAM {s.Memory.UsagePercent:F1}% / {_monitor.RamGuardrailPercent:F0}% · CPU {s.Cpu.TotalUsagePercent:F1}% / {_monitor.CpuGuardrailPercent:F0}%).";
            HealthBadgeBrush = WarningBrush;
        }
        else
        {
            HealthHeadline = "Sistem Sehat — Dalam Batas Target";
            HealthDetail = $"RAM ({s.Memory.UsagePercent:F1}% ≤ {_monitor.RamGuardrailPercent:F0}%) dan CPU ({s.Cpu.TotalUsagePercent:F1}% ≤ {_monitor.CpuGuardrailPercent:F0}%) beroperasi stabil dengan ruang lega.";
            HealthBadgeBrush = SuccessBrush;
        }

        int count = Math.Min(_coreValues.Count, s.Cpu.PerCoreUsagePercent.Length);
        for (int i = 0; i < count; i++)
        {
            _coreValues[i].Value = s.Cpu.PerCoreUsagePercent[i];
        }

        if (appendToSeries)
        {
            int maxPoints = GetMaxPointsForWindow();
            AppendBounded(_cpuPoints, s.Cpu.TotalUsagePercent, maxPoints);
            AppendBounded(_ramPoints, s.Memory.UsagePercent, maxPoints);
            AppendBounded(_diskActivePoints, s.Disk.ActiveTimePercent, maxPoints);
            AppendBounded(_netDownPoints, Math.Round(ByteFormatter.ToMegabytes(s.Network.DownloadBytesPerSec), 2), maxPoints);
        }

        TopProcesses.Clear();
        foreach (var proc in s.TopProcesses)
        {
            TopProcesses.Add(new TopProcessRow(
                DisplayName: proc.DisplayName,
                ProcessName: $"{proc.ProcessName}.exe",
                InstanceBadge: proc.InstanceCount > 1 ? $"{proc.InstanceCount} proses" : "1 proses",
                MemoryFormatted: ByteFormatter.FormatBytes(proc.PrivateMemoryBytes),
                CpuFormatted: $"{proc.CpuPercent:F1}%",
                ProtectionBadge: proc.IsProtected ? "Dilindungi" : "Standar"));
        }
    }

    private static void AppendBounded(ObservableCollection<ObservableValue> collection, double value, int maxPoints)
    {
        collection.Add(new ObservableValue(value));
        while (collection.Count > maxPoints)
        {
            collection.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
    }
}
