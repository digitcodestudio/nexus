using Nexus.Models;

namespace Nexus.Core.Contracts;

public interface IHardwareInfoProvider
{
    HardwareInfo GetHardwareInfo();
}

public interface ISystemMonitorService : IAsyncDisposable
{
    HardwareInfo HardwareInfo { get; }
    TimeSpan SamplingInterval { get; set; }
    double RamGuardrailPercent { get; set; }
    double CpuGuardrailPercent { get; set; }
    SystemSnapshot? LatestSnapshot { get; }
    bool IsRunning { get; }

    event EventHandler<SystemSnapshot>? SnapshotUpdated;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    IReadOnlyList<SystemSnapshot> GetHistory(TimeSpan window);
}

public interface IWindowsServiceManager
{
    Task<IReadOnlyList<WindowsServiceInfo>> GetAllServicesAsync(CancellationToken ct = default);
    Task<WindowsServiceInfo?> GetServiceDetailsAsync(string serviceName, CancellationToken ct = default);
    Task<ServiceDependencyGraph> GetDependencyGraphAsync(string serviceName, CancellationToken ct = default);
    Task<OperationResult> StartServiceAsync(string serviceName, CancellationToken ct = default);
    Task<OperationResult> StopServiceAsync(string serviceName, bool userConfirmed, CancellationToken ct = default);
    Task<OperationResult> RestartServiceAsync(string serviceName, bool userConfirmed, CancellationToken ct = default);
    Task<OperationResult> ChangeStartupTypeAsync(string serviceName, ServiceStartupType newType, bool userConfirmed, CancellationToken ct = default);
    Task<OperationResult> RollbackServiceAsync(string serviceName, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceConfigurationSnapshot>> GetBackupsAsync(CancellationToken ct = default);
}

public interface IProcessManagerService
{
    Task<IReadOnlyList<ProcessInfo>> GetRunningProcessesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationProcessGroup>> GetGroupedApplicationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ApplicationProcessGroup>> GetIdleCandidatesAsync(TimeSpan idleThreshold, CancellationToken ct = default);
    Task<OperationResult> ExecuteSafeActionAsync(int processId, SafeActionType action, bool userConfirmed, CancellationToken ct = default);
    Task<bool> IsProtectedAsync(string processName, CancellationToken ct = default);
    Task SetProtectionAsync(string processName, bool isProtected, CancellationToken ct = default);
}

public interface IMemoryOptimizer
{
    MemoryAnalysisReport Analyze(SystemSnapshot snapshot, IReadOnlyList<ApplicationProcessGroup> apps, double ramThresholdPercent = 75.0);
    IReadOnlyList<MemoryLeakSuspect> DetectLeakTrends();
    Task<OperationResult> ApplyScopedOptimizationAsync(int processId, bool userConfirmed, CancellationToken ct = default);
}

public interface ICpuOptimizer
{
    CpuAnalysisReport Analyze(SystemSnapshot snapshot, IReadOnlyList<ApplicationProcessGroup> apps, double cpuThresholdPercent = 65.0);
    Task<OperationResult> SetEcoQoSAsync(int processId, bool enable, CancellationToken ct = default);
    Task<OperationResult> SetProcessPriorityAsync(int processId, ProcessPriorityLevel priority, CancellationToken ct = default);
    Task<OperationResult> SetPowerModeAsync(WindowsPowerMode mode, CancellationToken ct = default);
}

public interface IOptimizationEngine
{
    OptimizationProfile ActiveProfile { get; }
    bool IsAutomationPaused { get; }
    bool IsSafeModeEnabled { get; set; }

    Task SetActiveProfileAsync(ProfileType profileType, CancellationToken ct = default);
    Task<IReadOnlyList<OptimizationRecommendation>> EvaluateRecommendationsAsync(CancellationToken ct = default);
    Task EmergencyStopAsync(CancellationToken ct = default);
    Task ResumeAutomationAsync(CancellationToken ct = default);
}

public interface ISafetyPolicyValidator
{
    OperationResult ValidateServiceMutation(string serviceName, ServiceDependencyGraph dependencies, bool userConfirmed);
    OperationResult ValidateProcessAction(ProcessInfo process, SafeActionType action, bool userConfirmed);
}

public interface INexusRepository
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);
    Task<IReadOnlyList<OptimizationProfile>> GetProfilesAsync(CancellationToken ct = default);
    Task SaveProfileAsync(OptimizationProfile profile, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetProtectedAppsAsync(CancellationToken ct = default);
    Task SetProtectedAppAsync(string processName, bool isProtected, CancellationToken ct = default);
    Task SaveServiceBackupAsync(ServiceConfigurationSnapshot snapshot, CancellationToken ct = default);
    Task<ServiceConfigurationSnapshot?> GetLatestServiceBackupAsync(string serviceName, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceConfigurationSnapshot>> GetAllServiceBackupsAsync(CancellationToken ct = default);
    Task MarkServiceBackupRolledBackAsync(Guid backupId, DateTimeOffset rolledBackUtc, CancellationToken ct = default);
    Task LogActivityAsync(ActivityLogEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<ActivityLogEntry>> GetActivityHistoryAsync(int limit = 200, CancellationToken ct = default);
    Task AppendMonitoringSampleAsync(SystemSnapshot snapshot, CancellationToken ct = default);
    Task PruneMonitoringHistoryAsync(int retentionDays, CancellationToken ct = default);
}

