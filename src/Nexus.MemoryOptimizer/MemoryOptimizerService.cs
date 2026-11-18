using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexus.Core.Contracts;
using Nexus.Core.Formatting;
using Nexus.Core.Safety;
using Nexus.Models;

namespace Nexus.MemoryOptimizer;

public sealed partial class MemoryOptimizerService : IMemoryOptimizer
{
    private readonly ILogger<MemoryOptimizerService> _logger;
    private readonly ConcurrentDictionary<int, (string Name, long InitialBytes, DateTimeOffset FirstSeenUtc, long LastBytes, int GrowthStreak)> _trendTracker = new();

    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSizeEx(nint hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize, uint flags);

    public MemoryOptimizerService(ILogger<MemoryOptimizerService> logger)
    {
        _logger = logger;
    }

    public MemoryAnalysisReport Analyze(SystemSnapshot snapshot, IReadOnlyList<ApplicationProcessGroup> apps, double ramThresholdPercent = 75.0)
    {
        var now = DateTimeOffset.UtcNow;
        var activePids = new HashSet<int>();

        foreach (var app in apps)
        {
            foreach (var proc in app.Processes)
            {
                activePids.Add(proc.ProcessId);
                _trendTracker.AddOrUpdate(
                    proc.ProcessId,
                    _ => (proc.ProcessName, proc.PrivateMemoryBytes, now, proc.PrivateMemoryBytes, 0),
                    (_, prev) =>
                    {
                        int streak = proc.PrivateMemoryBytes > prev.LastBytes + (2 * 1024 * 1024)
                            ? prev.GrowthStreak + 1
                            : Math.Max(0, prev.GrowthStreak - 1);
                        return (prev.Name, prev.InitialBytes, prev.FirstSeenUtc, proc.PrivateMemoryBytes, streak);
                    });
            }
        }

        foreach (var stale in _trendTracker.Keys.Where(id => !activePids.Contains(id)).ToList())
        {
            _trendTracker.TryRemove(stale, out _);
        }

        var leaks = DetectLeakTrends();
        var recommendations = new List<OptimizationRecommendation>();
        bool exceeded = snapshot.Memory.UsagePercent >= ramThresholdPercent;

        foreach (var app in apps.Where(a => !a.IsProtected && !a.IsAnyForeground).Take(6))
        {
            if (app.TotalPrivateMemoryBytes < 180L * 1024 * 1024) continue;

            bool isBrowser = app.GroupKey.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                             app.GroupKey.Equals("msedge", StringComparison.OrdinalIgnoreCase);

            if (exceeded || app.GroupIdleDuration >= TimeSpan.FromMinutes(15))
            {
                recommendations.Add(new OptimizationRecommendation(
                    Id: Guid.NewGuid(),
                    Category: RecommendationCategory.Memory,
                    Title: isBrowser
                        ? $"Grup Browser '{app.DisplayName}' menggunakan {ByteFormatter.FormatBytes(app.TotalPrivateMemoryBytes)} ({app.ProcessCount} proses)"
                        : $"Aplikasi latar belakang '{app.DisplayName}' memakai {ByteFormatter.FormatBytes(app.TotalPrivateMemoryBytes)}",
                    Description: exceeded
                        ? $"Konsumsi RAM ({snapshot.Memory.UsagePercent:F1}%) melebihi batas {ramThresholdPercent:F0}%. Aktifkan Mode Efisiensi (EcoQoS) atau tutup tab/proses latar belakang yang tidak dipakai."
                        : $"Aplikasi tidak aktif selama {app.GroupIdleDuration.TotalMinutes:F0} menit tanpa beban kerja penting.",
                    TargetName: app.GroupKey,
                    EstimatedReclaimableBytes: (long)(app.TotalPrivateMemoryBytes * 0.4),
                    SuggestedAction: SafeActionType.ApplyEcoQoS,
                    Risk: RiskLevel.Low,
                    RequiresElevation: false,
                    CreatedUtc: now));
            }
        }

        return new MemoryAnalysisReport(
            TimestampUtc: now,
            CurrentUsagePercent: snapshot.Memory.UsagePercent,
            GuardrailThresholdPercent: ramThresholdPercent,
            PressureLevel: snapshot.Memory.PressureLevel,
            IsGuardrailExceeded: exceeded,
            TopConsumers: apps.OrderByDescending(a => a.TotalPrivateMemoryBytes).Take(10).ToList(),
            LeakSuspects: leaks,
            Recommendations: recommendations);
    }

    public IReadOnlyList<MemoryLeakSuspect> DetectLeakTrends()
    {
        var now = DateTimeOffset.UtcNow;
        var suspects = new List<MemoryLeakSuspect>();

        foreach (var kv in _trendTracker)
        {
            var (name, initialBytes, firstSeen, lastBytes, streak) = kv.Value;
            var duration = now - firstSeen;
            if (duration < TimeSpan.FromMinutes(3) || streak < 3) continue;

            long delta = lastBytes - initialBytes;
            if (delta < 25L * 1024 * 1024) continue;

            double mbPerHour = ByteFormatter.ToMegabytes(delta) / Math.Max(0.05, duration.TotalHours);
            if (mbPerHour >= 40.0)
            {
                suspects.Add(new MemoryLeakSuspect(
                    ProcessId: kv.Key,
                    ProcessName: name,
                    InitialBytes: initialBytes,
                    CurrentBytes: lastBytes,
                    GrowthMegabytesPerHour: Math.Round(mbPerHour, 1),
                    ObservationDuration: duration));
            }
        }

        return suspects.OrderByDescending(s => s.GrowthMegabytesPerHour).ToList();
    }

    /// <summary>
    /// Scoped, user-confirmed working set hint for a single non-critical process only.
    /// NEVER performs blind global EmptyWorkingSet across all system processes.
    /// </summary>
    public Task<OperationResult> ApplyScopedOptimizationAsync(int processId, bool userConfirmed, CancellationToken ct = default)
    {
        if (!userConfirmed)
        {
            return Task.FromResult(OperationResult.Fail(
                "Optimasi memori terarah memerlukan konfirmasi eksplisit dari pengguna.",
                "CONFIRMATION_REQUIRED"));
        }

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(processId);
            if (ProtectedLists.IsImmutableSystemProcess(proc.ProcessName) || processId <= 4)
            {
                return Task.FromResult(OperationResult.Fail(
                    $"Proses sistem '{proc.ProcessName}' dilindungi dan tidak boleh dioptimasi secara paksa.",
                    "IMMUTABLE_SYSTEM_PROCESS"));
            }

            nint handle = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == nint.Zero)
            {
                return Task.FromResult(OperationResult.Fail(
                    $"Akses terbatas untuk mengoptimalkan '{proc.ProcessName}' (PID {processId}).",
                    "ACCESS_DENIED",
                    requiresElevation: true));
            }

            try
            {
                bool ok = SetProcessWorkingSetSizeEx(handle, -1, -1, 0);
                return Task.FromResult(ok
                    ? OperationResult.Ok($"Optimasi memori terarah berhasil dijalankan untuk '{proc.ProcessName}' (PID {processId}).")
                    : OperationResult.Fail($"Windows menolak penyesuaian working set untuk '{proc.ProcessName}'.", "WIN32_FAILED"));
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal optimasi memori terarah untuk PID {Pid}.", processId);
            return Task.FromResult(OperationResult.Fail($"Gagal mengoptimalkan PID {processId}: {ex.Message}", "MEMORY_OPT_ERROR"));
        }
    }
}

