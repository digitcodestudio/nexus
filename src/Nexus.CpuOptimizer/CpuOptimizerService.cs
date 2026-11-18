using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexus.Core.Contracts;
using Nexus.Core.Safety;
using Nexus.Models;

namespace Nexus.CpuOptimizer;

public sealed partial class CpuOptimizerService : ICpuOptimizer
{
    private readonly ILogger<CpuOptimizerService> _logger;
    private readonly ConcurrentDictionary<int, ProcessPriorityClass> _trackedEcoProcesses = new();

    private const uint PROCESS_SET_INFORMATION = 0x0200;
    private const int ProcessPowerThrottling = 4;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    private static readonly Guid PowerOverlayBetterBattery = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid PowerOverlayBalanced = new("00000000-0000-0000-0000-000000000000");
    private static readonly Guid PowerOverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(
        nint hProcess,
        int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation,
        uint processInformationSize);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveOverlayScheme(in Guid overlaySchemeGuid);

    public CpuOptimizerService(ILogger<CpuOptimizerService> logger)
    {
        _logger = logger;
    }

    public int ActiveEcoQoSCount => _trackedEcoProcesses.Count;

    public CpuAnalysisReport Analyze(SystemSnapshot snapshot, IReadOnlyList<ApplicationProcessGroup> apps, double cpuThresholdPercent = 65.0)
    {
        var now = DateTimeOffset.UtcNow;
        bool exceeded = snapshot.Cpu.TotalUsagePercent >= cpuThresholdPercent;

        var backgroundConsumers = apps
            .Where(a => !a.IsAnyForeground && !a.IsProtected && a.TotalCpuPercent >= 1.0)
            .OrderByDescending(a => a.TotalCpuPercent)
            .Take(8)
            .ToList();

        var recommendations = new List<OptimizationRecommendation>();
        foreach (var app in backgroundConsumers.Take(5))
        {
            if (exceeded || app.TotalCpuPercent >= 5.0)
            {
                recommendations.Add(new OptimizationRecommendation(
                    Id: Guid.NewGuid(),
                    Category: RecommendationCategory.Cpu,
                    Title: $"Beban CPU latar belakang '{app.DisplayName}' sebesar {app.TotalCpuPercent:F1}% ({app.ProcessCount} proses)",
                    Description: "Terapkan Mode Efisiensi Windows (EcoQoS) & prioritas BelowNormal agar aplikasi latar belakang tidak membebani CPU melewati batas 65%.",
                    TargetName: app.GroupKey,
                    EstimatedReclaimableBytes: null,
                    SuggestedAction: SafeActionType.ApplyEcoQoS,
                    Risk: RiskLevel.Low,
                    RequiresElevation: false,
                    CreatedUtc: now));
            }
        }

        return new CpuAnalysisReport(
            TimestampUtc: now,
            CurrentUsagePercent: snapshot.Cpu.TotalUsagePercent,
            GuardrailThresholdPercent: cpuThresholdPercent,
            IsSustainedHighLoad: snapshot.Cpu.IsSustainedHighLoad,
            IsGuardrailExceeded: exceeded,
            BackgroundLoadConsumers: backgroundConsumers,
            Recommendations: recommendations);
    }

    public Task<OperationResult> SetEcoQoSAsync(int processId, bool enable, CancellationToken ct = default)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            if (ProtectedLists.IsImmutableSystemProcess(proc.ProcessName) || processId <= 4)
            {
                return Task.FromResult(OperationResult.Fail(
                    $"Proses sistem '{proc.ProcessName}' tidak boleh diubah mode EcoQoS-nya.",
                    "IMMUTABLE_SYSTEM_PROCESS"));
            }

            nint handle = OpenProcess(PROCESS_SET_INFORMATION, false, processId);
            if (handle == nint.Zero)
            {
                return Task.FromResult(OperationResult.Fail(
                    $"Akses ditolak saat mengubah EcoQoS pada '{proc.ProcessName}' (PID {processId}).",
                    "ACCESS_DENIED",
                    requiresElevation: true));
            }

            try
            {
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = enable ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
                };

                bool ok = SetProcessInformation(
                    handle,
                    ProcessPowerThrottling,
                    ref state,
                    (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());

                if (!ok)
                {
                    return Task.FromResult(OperationResult.Fail(
                        $"Windows tidak mengizinkan pengaturan EcoQoS pada '{proc.ProcessName}'.",
                        "ECOQOS_FAILED"));
                }

                if (enable)
                {
                    _trackedEcoProcesses.TryAdd(processId, proc.PriorityClass);
                    try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    return Task.FromResult(OperationResult.Ok($"Mode Efisiensi (EcoQoS) aktif pada '{proc.ProcessName}' (PID {processId})."));
                }
                else
                {
                    if (_trackedEcoProcesses.TryRemove(processId, out var origPriority))
                    {
                        try { proc.PriorityClass = origPriority; } catch { }
                    }
                    return Task.FromResult(OperationResult.Ok($"Mode Efisiensi (EcoQoS) dinonaktifkan untuk '{proc.ProcessName}' (PID {processId})."));
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal mengubah EcoQoS untuk PID {Pid}.", processId);
            return Task.FromResult(OperationResult.Fail($"Gagal mengatur EcoQoS: {ex.Message}", "CPU_OPT_ERROR"));
        }
    }

    public Task<OperationResult> SetProcessPriorityAsync(int processId, ProcessPriorityLevel priority, CancellationToken ct = default)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            if (ProtectedLists.IsImmutableSystemProcess(proc.ProcessName) || processId <= 4)
            {
                return Task.FromResult(OperationResult.Fail(
                    $"Prioritas proses sistem '{proc.ProcessName}' dilindungi.",
                    "IMMUTABLE_SYSTEM_PROCESS"));
            }

            proc.PriorityClass = priority switch
            {
                ProcessPriorityLevel.Idle => ProcessPriorityClass.Idle,
                ProcessPriorityLevel.BelowNormal => ProcessPriorityClass.BelowNormal,
                ProcessPriorityLevel.Normal => ProcessPriorityClass.Normal,
                ProcessPriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
                ProcessPriorityLevel.High => ProcessPriorityClass.High,
                _ => ProcessPriorityClass.Normal
            };

            return Task.FromResult(OperationResult.Ok($"Prioritas '{proc.ProcessName}' diubah ke {priority}."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail($"Gagal mengubah prioritas proses: {ex.Message}", "PRIORITY_ERROR"));
        }
    }

    public Task<OperationResult> SetPowerModeAsync(WindowsPowerMode mode, CancellationToken ct = default)
    {
        try
        {
            Guid target = mode switch
            {
                WindowsPowerMode.BestPowerEfficiency => PowerOverlayBetterBattery,
                WindowsPowerMode.BestPerformance => PowerOverlayBestPerformance,
                _ => PowerOverlayBalanced
            };

            uint status = PowerSetActiveOverlayScheme(in target);
            return Task.FromResult(status == 0
                ? OperationResult.Ok($"Mode daya Windows diatur ke '{mode}'.")
                : OperationResult.Fail(
                    $"Mode daya overlay tidak didukung pada skema daya saat ini (Kode: {status}). Pastikan Windows Power Plan berada pada 'Balanced'.",
                    $"POWER_{status}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail($"Gagal mengatur mode daya Windows: {ex.Message}", "POWER_ERROR"));
        }
    }

    public async Task RevertAllEcoQoSAsync()
    {
        foreach (int pid in _trackedEcoProcesses.Keys.ToList())
        {
            await SetEcoQoSAsync(pid, enable: false);
        }
        _trackedEcoProcesses.Clear();
    }
}

