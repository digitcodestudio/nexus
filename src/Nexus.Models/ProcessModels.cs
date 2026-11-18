namespace Nexus.Models;

public enum ProcessActivityStatus
{
    ActiveForeground,
    ActiveBackgroundWork,
    Idle,
    Protected,
    System
}

public enum SafeActionType
{
    Ignore,
    Notify,
    ApplyEcoQoS,
    Minimize,
    GracefulClose,
    Suspend
}

public enum ProcessPriorityLevel
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High
}

public sealed record ProcessInfo(
    int ProcessId,
    int ParentProcessId,
    string ProcessName,
    string ApplicationName,
    string? ExecutablePath,
    double CpuPercent,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    DateTimeOffset? StartTimeUtc,
    bool IsForeground,
    bool HasVisibleWindow,
    string? MainWindowTitle,
    TimeSpan IdleDuration,
    ProcessActivityStatus ActivityStatus,
    bool IsProtected,
    bool IsImmutableSystem,
    bool IsEcoQoSActive);

public sealed record ApplicationProcessGroup(
    string GroupKey,
    string DisplayName,
    int ProcessCount,
    double TotalCpuPercent,
    long TotalWorkingSetBytes,
    long TotalPrivateMemoryBytes,
    bool IsAnyForeground,
    bool HasActiveBackgroundWork,
    TimeSpan GroupIdleDuration,
    bool IsProtected,
    IReadOnlyList<ProcessInfo> Processes);

