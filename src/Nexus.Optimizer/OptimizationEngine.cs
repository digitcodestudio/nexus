using Microsoft.Extensions.Logging;
using Nexus.Core.Contracts;
using Nexus.Core.Safety;
using Nexus.CpuOptimizer;
using Nexus.Models;
using Nexus.ProcessManager;

namespace Nexus.Optimizer;

public sealed class OptimizationEngine : IOptimizationEngine
{
    private readonly ISystemMonitorService _monitor;
    private readonly IProcessManagerService _processManager;
    private readonly IWindowsServiceManager _serviceManager;
    private readonly IMemoryOptimizer _memoryOptimizer;
    private readonly ICpuOptimizer _cpuOptimizer;
    private readonly TrayHygieneService _trayHygiene;
    private readonly INexusRepository _repository;
    private readonly ILogger<OptimizationEngine> _logger;

    private List<OptimizationProfile> _profiles = [];
    private OptimizationProfile _activeProfile;

    public OptimizationProfile ActiveProfile => _activeProfile;
    public IReadOnlyList<OptimizationProfile> Profiles => _profiles;
    public bool IsAutomationPaused { get; private set; }
    public bool IsSafeModeEnabled { get; set; } = true;
    public bool EnableAutoTrayCleanup { get; set; } = true;
    public bool EnableAutoStopUnusedServices { get; set; }

    public event EventHandler<OptimizationProfile>? ProfileChanged;

    public OptimizationEngine(
        ISystemMonitorService monitor,
        IProcessManagerService processManager,
        IWindowsServiceManager serviceManager,
        IMemoryOptimizer memoryOptimizer,
        ICpuOptimizer cpuOptimizer,
        TrayHygieneService trayHygiene,
        INexusRepository repository,
        ILogger<OptimizationEngine> logger)
    {
        _monitor = monitor;
        _processManager = processManager;
        _serviceManager = serviceManager;
        _memoryOptimizer = memoryOptimizer;
        _cpuOptimizer = cpuOptimizer;
        _trayHygiene = trayHygiene;
        _repository = repository;
        _logger = logger;

        _activeProfile = new OptimizationProfile(
            Guid.Parse("11111111-1111-1111-1111-111111111101"),
            ProfileType.Balanced,
            "Seimbang",
            "Perilaku Windows normal dengan optimasi latar belakang moderat (RAM ≤ 75%, CPU ≤ 65%).",
            GuardrailDefaults.DefaultRamThresholdPercent,
            GuardrailDefaults.DefaultCpuThresholdPercent,
            GuardrailDefaults.DefaultSustainedCpuSeconds,
            GuardrailDefaults.DefaultIdleTimeoutMinutes,
            true, true, true, WindowsPowerMode.Balanced);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _repository.InitializeAsync(ct);
        _profiles = (await _repository.GetProfilesAsync(ct)).ToList();

        string? savedType = await _repository.GetSettingAsync("profile.active", ct);
        if (Enum.TryParse<ProfileType>(savedType, out var parsed))
        {
            var match = _profiles.FirstOrDefault(p => p.Type == parsed);
            if (match is not null) _activeProfile = match;
        }
        else if (_profiles.Count > 0)
        {
            _activeProfile = _profiles[0];
        }

        string? safeMode = await _repository.GetSettingAsync("automation.safe_mode", ct);
        IsSafeModeEnabled = bool.TryParse(safeMode, out bool parsedSafe) ? parsedSafe : _activeProfile.SafeModeOnly;

        string? autoTray = await _repository.GetSettingAsync("automation.auto_tray_cleanup", ct);
        EnableAutoTrayCleanup = !bool.TryParse(autoTray, out bool parsedTray) || parsedTray;

        string? autoSvc = await _repository.GetSettingAsync("automation.auto_stop_unused_services", ct);
        EnableAutoStopUnusedServices = bool.TryParse(autoSvc, out bool parsedSvc) && parsedSvc;

        ApplyProfileToMonitor(_activeProfile);
    }

    public async Task SetActiveProfileAsync(ProfileType profileType, CancellationToken ct = default)
    {
        if (_profiles.Count == 0)
        {
            _profiles = (await _repository.GetProfilesAsync(ct)).ToList();
        }

        var found = _profiles.FirstOrDefault(p => p.Type == profileType);
        if (found is null) return;

        _activeProfile = found;
        ApplyProfileToMonitor(found);
        await _repository.SetSettingAsync("profile.active", profileType.ToString(), ct);
        await _repository.LogActivityAsync(new ActivityLogEntry(
            0, DateTimeOffset.UtcNow, ActivitySeverity.Info, "Optimizer", "SwitchProfile", found.Name,
            $"Profil optimasi diubah ke '{found.Name}' (Target RAM ≤ {found.RamThresholdPercent:F0}%, CPU ≤ {found.CpuThresholdPercent:F0}%).",
            false, null), ct);

        ProfileChanged?.Invoke(this, found);
    }

    public async Task UpdateProfileAsync(OptimizationProfile updated, CancellationToken ct = default)
    {
        await _repository.SaveProfileAsync(updated, ct);
        int idx = _profiles.FindIndex(p => p.Type == updated.Type);
        if (idx >= 0) _profiles[idx] = updated;

        if (_activeProfile.Type == updated.Type)
        {
            _activeProfile = updated;
            ApplyProfileToMonitor(updated);
            ProfileChanged?.Invoke(this, updated);
        }
    }

    public Task<TrayCleanupReport> CleanTrayAndDuplicatesAsync(CancellationToken ct = default)
        => _trayHygiene.CleanupDuplicatesAndGhostIconsAsync(closeDuplicateInstances: true, ct);

    /// <summary>
    /// Safely stops running non-critical optimization candidate services (e.g. vendor/browser updaters,
    /// telemetry, retail demo, unused Xbox helpers) with mandatory SQLite rollback snapshots.
    /// NEVER stops critical Windows services.
    /// </summary>
    public async Task<OperationResult> StopUnusedCandidateServicesAsync(bool userConfirmed, CancellationToken ct = default)
    {
        if (!userConfirmed)
        {
            return OperationResult.Fail("Konfirmasi eksplisit diperlukan untuk menghentikan layanan non-kritis.", "CONFIRMATION_REQUIRED");
        }

        var services = await _serviceManager.GetAllServicesAsync(ct);
        var candidates = services
            .Where(s => !s.IsCriticalProtected &&
                        s.State == ServiceState.Running &&
                        s.Recommendation == ServiceRecommendation.OptInOptimizationCandidate)
            .ToList();

        if (candidates.Count == 0)
        {
            return OperationResult.Ok("Tidak ada layanan kandidat non-kritis yang sedang berjalan.");
        }

        int stopped = 0;
        int needAdmin = 0;
        foreach (var svc in candidates)
        {
            var res = await _serviceManager.StopServiceAsync(svc.ServiceName, userConfirmed: true, ct);
            if (res.Succeeded) stopped++;
            else if (res.RequiresElevation) needAdmin++;
        }

        if (stopped == 0 && needAdmin > 0)
        {
            return OperationResult.Fail(
                $"Ditemukan {candidates.Count} layanan kandidat, tetapi diperlukan hak Administrator (Jalankan NEXUS sebagai Admin) untuk menghentikannya.",
                "ACCESS_DENIED",
                requiresElevation: true);
        }

        return OperationResult.Ok($"Berhasil menghentikan {stopped} dari {candidates.Count} layanan non-kritis (semua cadangan Rollback tersimpan di SQLite).");
    }

    public async Task<IReadOnlyList<OptimizationRecommendation>> EvaluateRecommendationsAsync(CancellationToken ct = default)
    {
        var snapshot = _monitor.LatestSnapshot;
        if (snapshot is null) return [];

        // Auto Tray Hygiene (purges ghost icons & closes duplicate idle Telegram/chat tray copies)
        if (!IsAutomationPaused && EnableAutoTrayCleanup)
        {
            await _trayHygiene.CleanupDuplicatesAndGhostIconsAsync(
                closeDuplicateInstances: !IsSafeModeEnabled,
                ct);
        }

        var groups = await _processManager.GetGroupedApplicationsAsync(ct);
        var memReport = _memoryOptimizer.Analyze(snapshot, groups, _activeProfile.RamThresholdPercent);
        var cpuReport = _cpuOptimizer.Analyze(snapshot, groups, _activeProfile.CpuThresholdPercent);

        var combined = new List<OptimizationRecommendation>();
        combined.AddRange(memReport.Recommendations);
        combined.AddRange(cpuReport.Recommendations);

        if (_activeProfile.EnableServiceRecommendations)
        {
            var services = await _serviceManager.GetAllServicesAsync(ct);
            var runningCandidates = services.Where(s =>
                         s.Recommendation == ServiceRecommendation.OptInOptimizationCandidate &&
                         s.State == ServiceState.Running &&
                         !s.IsCriticalProtected).Take(6).ToList();

            foreach (var svc in runningCandidates)
            {
                combined.Add(new OptimizationRecommendation(
                    Id: Guid.NewGuid(),
                    Category: RecommendationCategory.Service,
                    Title: $"Layanan kandidat optimasi berjalan: '{svc.DisplayName}' ({svc.ServiceName})",
                    Description: "Layanan non-kritis (updater vendor/browser atau fitur opsional tidak terpakai) aman dihentikan dengan snapshot rollback otomatis.",
                    TargetName: svc.ServiceName,
                    EstimatedReclaimableBytes: null,
                    SuggestedAction: SafeActionType.Notify,
                    Risk: RiskLevel.Low,
                    RequiresElevation: true,
                    CreatedUtc: DateTimeOffset.UtcNow));

                // Opt-in safe automatic stop of explicitly non-critical candidate services ONLY when enabled & Safe Mode OFF
                if (!IsAutomationPaused && !IsSafeModeEnabled && EnableAutoStopUnusedServices)
                {
                    await _serviceManager.StopServiceAsync(svc.ServiceName, userConfirmed: true, ct);
                }
            }
        }

        // Apply automatic reversible EcoQoS ONLY when automation is active and Safe Mode is OFF
        if (!IsAutomationPaused && !IsSafeModeEnabled && _activeProfile.EnableEcoQoS &&
            (snapshot.Memory.IsAboveGuardrail || snapshot.Cpu.IsSustainedHighLoad))
        {
            var idleCandidates = groups
                .Where(g => !g.IsProtected && !g.IsAnyForeground && !g.HasActiveBackgroundWork &&
                            g.GroupIdleDuration >= TimeSpan.FromMinutes(Math.Min(15, _activeProfile.IdleTimeoutMinutes)))
                .Take(3);

            foreach (var candidate in idleCandidates)
            {
                foreach (var proc in candidate.Processes.Where(p => !p.IsEcoQoSActive).Take(6))
                {
                    await _cpuOptimizer.SetEcoQoSAsync(proc.ProcessId, enable: true, ct);
                }

                await _repository.LogActivityAsync(new ActivityLogEntry(
                    0, DateTimeOffset.UtcNow, ActivitySeverity.Success, "SmartAutomation", "AutoEcoQoS", candidate.DisplayName,
                    $"Otomatisasi menerapkan Mode Efisiensi (EcoQoS) pada '{candidate.DisplayName}' ({candidate.ProcessCount} proses) karena beban melebihi batas target.",
                    true, null), ct);
            }
        }

        return combined;
    }

    public async Task EmergencyStopAsync(CancellationToken ct = default)
    {
        IsAutomationPaused = true;
        IsSafeModeEnabled = true;
        EnableAutoStopUnusedServices = false;

        if (_cpuOptimizer is CpuOptimizerService cpuSvc)
        {
            await cpuSvc.RevertAllEcoQoSAsync();
        }

        await _repository.SetSettingAsync("automation.safe_mode", "true", ct);
        await _repository.SetSettingAsync("automation.auto_stop_unused_services", "false", ct);
        await _repository.LogActivityAsync(new ActivityLogEntry(
            0, DateTimeOffset.UtcNow, ActivitySeverity.Warning, "SmartAutomation", "EmergencyStop", "Semua Aturan",
            "STOP DARURAT diaktifkan: seluruh otomatisasi dijeda, Mode Aman diaktifkan, dan seluruh EcoQoS dikembalikan ke normal.",
            false, null), ct);

        _logger.LogWarning("NEXUS Emergency Stop triggered by user.");
    }

    public async Task ResumeAutomationAsync(CancellationToken ct = default)
    {
        IsAutomationPaused = false;
        await _repository.LogActivityAsync(new ActivityLogEntry(
            0, DateTimeOffset.UtcNow, ActivitySeverity.Info, "SmartAutomation", "ResumeAutomation", "Semua Aturan",
            "Otomatisasi NEXUS dilanjutkan kembali.",
            false, null), ct);
    }

    private void ApplyProfileToMonitor(OptimizationProfile profile)
    {
        _monitor.RamGuardrailPercent = profile.RamThresholdPercent;
        _monitor.CpuGuardrailPercent = profile.CpuThresholdPercent;
    }
}
