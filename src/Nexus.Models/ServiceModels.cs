namespace Nexus.Models;

public enum ServiceState
{
    Unknown,
    Running,
    Stopped,
    StartPending,
    StopPending,
    Paused,
    ContinuePending,
    PausePending
}

public enum ServiceStartupType
{
    Unknown,
    Automatic,
    AutomaticDelayedStart,
    Manual,
    Disabled,
    Boot,
    System
}

public enum ServiceCategory
{
    CriticalWindows,
    Security,
    Networking,
    Hardware,
    Development,
    Optional,
    UserInstalled,
    OptimizationCandidate
}

public enum ServiceRecommendation
{
    KeepProtected,
    KeepDefault,
    ReviewManual,
    OptInOptimizationCandidate
}

public sealed record WindowsServiceInfo(
    string ServiceName,
    string DisplayName,
    string Description,
    ServiceState State,
    ServiceStartupType StartupType,
    int? ProcessId,
    string? BinaryPath,
    bool IsTriggerStart,
    string? ServiceGroup,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> DependentServices,
    ServiceCategory Category,
    ServiceRecommendation Recommendation,
    bool IsCriticalProtected,
    DateTimeOffset? LastKnownActivityUtc);

public sealed record ServiceDependencyGraph(
    string ServiceName,
    IReadOnlyList<string> DirectDependencies,
    IReadOnlyList<string> AllDependents,
    bool HasCriticalDependents);

public sealed record ServiceConfigurationSnapshot(
    Guid Id,
    string ServiceName,
    ServiceStartupType OriginalStartupType,
    bool OriginalDelayedAutoStart,
    ServiceState OriginalState,
    DateTimeOffset CapturedUtc,
    string Reason,
    DateTimeOffset? RolledBackUtc);

