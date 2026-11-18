namespace Nexus.Models;

public enum ProfileType
{
    Balanced,
    Development,
    HeavyWorkload,
    PowerSaving
}

public enum WindowsPowerMode
{
    BestPowerEfficiency,
    Balanced,
    BestPerformance
}

public enum RuleTriggerType
{
    ApplicationIdle,
    RamThresholdExceeded,
    CpuThresholdExceeded,
    ServiceIdleCandidate
}

public enum RecommendationCategory
{
    Memory,
    Cpu,
    Process,
    Service
}

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public enum ActivitySeverity
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record OptimizationProfile(
    Guid Id,
    ProfileType Type,
    string Name,
    string Description,
    double RamThresholdPercent,
    double CpuThresholdPercent,
    int SustainedCpuSeconds,
    int IdleTimeoutMinutes,
    bool EnableEcoQoS,
    bool EnableServiceRecommendations,
    bool SafeModeOnly,
    WindowsPowerMode TargetPowerMode);

public sealed record AutomationRule(
    Guid Id,
    string Name,
    bool IsEnabled,
    RuleTriggerType Trigger,
    double? ThresholdPercent,
    int? IdleMinutes,
    IReadOnlyList<string> SafetyChecks,
    SafeActionType Action,
    int CooldownMinutes,
    string? TargetFilter);

public sealed record OptimizationRecommendation(
    Guid Id,
    RecommendationCategory Category,
    string Title,
    string Description,
    string TargetName,
    long? EstimatedReclaimableBytes,
    SafeActionType SuggestedAction,
    RiskLevel Risk,
    bool RequiresElevation,
    DateTimeOffset CreatedUtc);

public sealed record MemoryLeakSuspect(
    int ProcessId,
    string ProcessName,
    long InitialBytes,
    long CurrentBytes,
    double GrowthMegabytesPerHour,
    TimeSpan ObservationDuration);

public sealed record MemoryAnalysisReport(
    DateTimeOffset TimestampUtc,
    double CurrentUsagePercent,
    double GuardrailThresholdPercent,
    MemoryPressureLevel PressureLevel,
    bool IsGuardrailExceeded,
    IReadOnlyList<ApplicationProcessGroup> TopConsumers,
    IReadOnlyList<MemoryLeakSuspect> LeakSuspects,
    IReadOnlyList<OptimizationRecommendation> Recommendations);

public sealed record CpuAnalysisReport(
    DateTimeOffset TimestampUtc,
    double CurrentUsagePercent,
    double GuardrailThresholdPercent,
    bool IsSustainedHighLoad,
    bool IsGuardrailExceeded,
    IReadOnlyList<ApplicationProcessGroup> BackgroundLoadConsumers,
    IReadOnlyList<OptimizationRecommendation> Recommendations);

public sealed record ActivityLogEntry(
    long Id,
    DateTimeOffset TimestampUtc,
    ActivitySeverity Severity,
    string Module,
    string Action,
    string Target,
    string Summary,
    bool IsReversible,
    Guid? RollbackReferenceId);

public sealed record OperationResult(
    bool Succeeded,
    string Message,
    bool RequiresElevation = false,
    string? ErrorCode = null,
    Guid? RollbackReferenceId = null)
{
    public static OperationResult Ok(string message, Guid? rollbackId = null)
        => new(true, message, false, null, rollbackId);

    public static OperationResult Fail(string message, string? errorCode = null, bool requiresElevation = false)
        => new(false, message, requiresElevation, errorCode, null);
}

