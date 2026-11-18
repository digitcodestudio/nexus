namespace Nexus.Models;

public enum MemoryPressureLevel
{
    Normal,
    Elevated,
    Pressure,
    Critical
}

public sealed record HardwareInfo(
    string CpuName,
    int PhysicalCores,
    int LogicalProcessors,
    long TotalPhysicalMemoryBytes,
    string WindowsEdition,
    string WindowsVersion,
    int OsBuildNumber,
    string Architecture);

public sealed record CpuMetrics(
    double TotalUsagePercent,
    double[] PerCoreUsagePercent,
    double? CurrentFrequencyMhz,
    double? TemperatureCelsius,
    bool IsSustainedHighLoad,
    bool IsAboveGuardrail);

public sealed record MemoryMetrics(
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes,
    long CachedBytes,
    long CommittedBytes,
    long CommitLimitBytes,
    double UsagePercent,
    MemoryPressureLevel PressureLevel,
    bool IsAboveGuardrail);

public sealed record DiskMetrics(
    double ActiveTimePercent,
    long ReadBytesPerSec,
    long WriteBytesPerSec,
    long SystemDriveUsedBytes,
    long SystemDriveTotalBytes,
    string SystemDriveLetter);

public sealed record NetworkMetrics(
    long DownloadBytesPerSec,
    long UploadBytesPerSec,
    string ActiveAdapterName);

public sealed record TopProcessSnapshot(
    int ProcessId,
    string ProcessName,
    string DisplayName,
    double CpuPercent,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    int InstanceCount,
    bool IsProtected);

public sealed record SystemSnapshot(
    DateTimeOffset TimestampUtc,
    CpuMetrics Cpu,
    MemoryMetrics Memory,
    DiskMetrics Disk,
    NetworkMetrics Network,
    int RunningProcessCount,
    int RunningServiceCount,
    TimeSpan SystemUptime,
    IReadOnlyList<TopProcessSnapshot> TopProcesses);

