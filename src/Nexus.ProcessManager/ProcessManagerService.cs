using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexus.Core.Contracts;
using Nexus.Core.Safety;
using Nexus.Models;
using Nexus.ProcessManager.Native;

namespace Nexus.ProcessManager;

public sealed class ProcessManagerService : IProcessManagerService
{
    private readonly ISafetyPolicyValidator _safetyValidator;
    private readonly INexusRepository _repository;
    private readonly ILogger<ProcessManagerService> _logger;
    private readonly int _logicalCores = Math.Max(1, Environment.ProcessorCount);

    private readonly ConcurrentDictionary<int, (TimeSpan CpuTime, ulong IoBytes, DateTimeOffset Timestamp)> _prevSamples = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastForegroundByGroup = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _friendlyNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, bool> _ecoQoSActivePids = new();

    private HashSet<string> _userProtectedApps = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _whitelistLoadedUtc = DateTimeOffset.MinValue;

    public ProcessManagerService(
        ISafetyPolicyValidator safetyValidator,
        INexusRepository repository,
        ILogger<ProcessManagerService> logger)
    {
        _safetyValidator = safetyValidator;
        _repository = repository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProcessInfo>> GetRunningProcessesAsync(CancellationToken ct = default)
    {
        await EnsureWhitelistLoadedAsync(ct);
        return await Task.Run(() => CaptureProcesses(ct), ct);
    }

    public async Task<IReadOnlyList<ApplicationProcessGroup>> GetGroupedApplicationsAsync(CancellationToken ct = default)
    {
        var processes = await GetRunningProcessesAsync(ct);
        return GroupProcesses(processes);
    }

    public async Task<IReadOnlyList<ApplicationProcessGroup>> GetIdleCandidatesAsync(TimeSpan idleThreshold, CancellationToken ct = default)
    {
        var groups = await GetGroupedApplicationsAsync(ct);
        return groups
            .Where(g => !g.IsProtected &&
                        !g.IsAnyForeground &&
                        !g.HasActiveBackgroundWork &&
                        g.GroupIdleDuration >= idleThreshold)
            .OrderByDescending(g => g.TotalPrivateMemoryBytes)
            .ToList();
    }

    public async Task<OperationResult> ExecuteSafeActionAsync(int processId, SafeActionType action, bool userConfirmed, CancellationToken ct = default)
    {
        var list = await GetRunningProcessesAsync(ct);
        var proc = list.FirstOrDefault(p => p.ProcessId == processId);
        if (proc is null)
            return OperationResult.Fail($"Proses PID {processId} sudah tidak berjalan.", "NOT_FOUND");

        var validation = _safetyValidator.ValidateProcessAction(proc, action, userConfirmed);
        if (!validation.Succeeded) return validation;

        try
        {
            switch (action)
            {
                case SafeActionType.Ignore:
                    return OperationResult.Ok($"Proses '{proc.ApplicationName}' diabaikan.");

                case SafeActionType.Notify:
                    await LogAsync(ActivitySeverity.Info, "Notify", proc.ProcessName,
                        $"Pengingat: '{proc.ApplicationName}' (PID {processId}) tidak aktif selama {proc.IdleDuration.TotalMinutes:F0} menit.", ct);
                    return OperationResult.Ok($"Notifikasi dicatat untuk '{proc.ApplicationName}'.");

                case SafeActionType.ApplyEcoQoS:
                    bool ecoOk = SetProcessEcoQoS(processId, enable: true);
                    if (!ecoOk)
                        return OperationResult.Fail($"Gagal menerapkan Mode Efisiensi (EcoQoS) ke PID {processId} (akses terbatas).", "ACCESS_DENIED");
                    _ecoQoSActivePids[processId] = true;
                    await LogAsync(ActivitySeverity.Success, "ApplyEcoQoS", proc.ProcessName,
                        $"Mode Efisiensi (EcoQoS) aktif untuk '{proc.ApplicationName}' (PID {processId}).", ct);
                    return OperationResult.Ok($"Mode Efisiensi (EcoQoS) berhasil diterapkan ke '{proc.ApplicationName}'.");

                case SafeActionType.Minimize:
                    using (var p = Process.GetProcessById(processId))
                    {
                        if (p.MainWindowHandle == nint.Zero)
                            return OperationResult.Fail($"'{proc.ApplicationName}' tidak memiliki jendela utama untuk diminimalkan.", "NO_WINDOW");
                        ProcessInterop.ShowWindow(p.MainWindowHandle, ProcessInterop.SW_MINIMIZE);
                    }
                    await LogAsync(ActivitySeverity.Info, "Minimize", proc.ProcessName, $"Jendela '{proc.ApplicationName}' diminimalkan.", ct);
                    return OperationResult.Ok($"Jendela '{proc.ApplicationName}' berhasil diminimalkan.");

                case SafeActionType.GracefulClose:
                    using (var p = Process.GetProcessById(processId))
                    {
                        if (p.MainWindowHandle == nint.Zero)
                            return OperationResult.Fail($"'{proc.ApplicationName}' tidak memiliki jendela utama untuk tutup halus (WM_CLOSE).", "NO_WINDOW");
                        ProcessInterop.PostMessageW(p.MainWindowHandle, ProcessInterop.WM_CLOSE, nint.Zero, nint.Zero);
                    }
                    await LogAsync(ActivitySeverity.Warning, "GracefulClose", proc.ProcessName,
                        $"Permintaan tutup halus (WM_CLOSE) dikirim ke '{proc.ApplicationName}' (PID {processId}).", ct);
                    return OperationResult.Ok($"Permintaan tutup halus (WM_CLOSE) dikirim ke '{proc.ApplicationName}'.");

                default:
                    return OperationResult.Fail($"Aksi '{action}' tidak diaktifkan demi stabilitas sistem.", "UNSUPPORTED_ACTION");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal menjalankan aksi {Action} pada PID {Pid}.", action, processId);
            return OperationResult.Fail($"Gagal menjalankan aksi pada '{proc.ApplicationName}': {ex.Message}", "PROCESS_ACTION_FAILED");
        }
    }

    public async Task<bool> IsProtectedAsync(string processName, CancellationToken ct = default)
    {
        await EnsureWhitelistLoadedAsync(ct);
        string norm = ProtectedLists.NormalizeProcessName(processName);
        return ProtectedLists.IsImmutableSystemProcess(norm) || _userProtectedApps.Contains(norm);
    }

    public async Task SetProtectionAsync(string processName, bool isProtected, CancellationToken ct = default)
    {
        string norm = ProtectedLists.NormalizeProcessName(processName);
        if (ProtectedLists.IsImmutableSystemProcess(norm)) return;

        await _repository.SetProtectedAppAsync(norm, isProtected, ct);
        if (isProtected) _userProtectedApps.Add(norm);
        else _userProtectedApps.Remove(norm);

        await LogAsync(ActivitySeverity.Info, isProtected ? "WhitelistAdd" : "WhitelistRemove", norm,
            isProtected ? $"'{norm}' ditambahkan ke daftar perlindungan." : $"'{norm}' dihapus dari daftar perlindungan.", ct);
    }

    public static bool SetProcessEcoQoS(int processId, bool enable)
    {
        using var handle = ProcessInterop.OpenProcess(ProcessInterop.PROCESS_SET_INFORMATION, false, processId);
        if (handle.IsInvalid) return false;

        var state = new ProcessInterop.PROCESS_POWER_THROTTLING_STATE
        {
            Version = ProcessInterop.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = ProcessInterop.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = enable ? ProcessInterop.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
        };

        return ProcessInterop.SetProcessInformation(
            handle,
            ProcessInterop.ProcessPowerThrottling,
            ref state,
            (uint)Marshal.SizeOf<ProcessInterop.PROCESS_POWER_THROTTLING_STATE>());
    }

    public static IReadOnlyList<ApplicationProcessGroup> GroupProcesses(IReadOnlyList<ProcessInfo> processes)
    {
        var byKey = processes.GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase);
        var groups = new List<ApplicationProcessGroup>();

        foreach (var grp in byKey)
        {
            var members = grp.ToList();
            var representative = members.FirstOrDefault(m => m.HasVisibleWindow) ?? members[0];
            bool anyFg = members.Any(m => m.IsForeground);
            bool anyProtected = members.Any(m => m.IsProtected || m.IsImmutableSystem);
            bool anyBuildChild = members.Any(m => ProtectedLists.ActiveBuildAndWorkProcessNames.Contains(m.ProcessName));
            double totalCpu = Math.Round(members.Sum(m => m.CpuPercent), 1);
            bool activeWork = anyFg || anyBuildChild || totalCpu >= 1.5 ||
                              members.Any(m => m.ActivityStatus == ProcessActivityStatus.ActiveBackgroundWork);

            TimeSpan minIdle = anyFg || activeWork
                ? TimeSpan.Zero
                : members.Min(m => m.IdleDuration);

            groups.Add(new ApplicationProcessGroup(
                GroupKey: grp.Key,
                DisplayName: representative.ApplicationName,
                ProcessCount: members.Count,
                TotalCpuPercent: totalCpu,
                TotalWorkingSetBytes: members.Sum(m => m.WorkingSetBytes),
                TotalPrivateMemoryBytes: members.Sum(m => m.PrivateMemoryBytes),
                IsAnyForeground: anyFg,
                HasActiveBackgroundWork: activeWork,
                GroupIdleDuration: minIdle,
                IsProtected: anyProtected,
                Processes: members));
        }

        return groups.OrderByDescending(g => g.TotalPrivateMemoryBytes).ToList();
    }

    private IReadOnlyList<ProcessInfo> CaptureProcesses(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var parentMap = ReadParentPidMap();

        int foregroundPid = 0;
        nint fgHwnd = ProcessInterop.GetForegroundWindow();
        if (fgHwnd != nint.Zero)
        {
            ProcessInterop.GetWindowThreadProcessId(fgHwnd, out foregroundPid);
        }

        TimeSpan userInputIdle = ReadSystemUserIdleTime();
        var rawProcesses = Process.GetProcesses();
        var activePids = new HashSet<int>(rawProcesses.Length);
        var results = new List<ProcessInfo>(rawProcesses.Length);

        foreach (var proc in rawProcesses)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                int pid = proc.Id;
                if (pid == 0) continue;

                activePids.Add(pid);
                string name = proc.ProcessName;
                int parentPid = parentMap.GetValueOrDefault(pid, 0);
                long ws = proc.WorkingSet64;
                long priv = proc.PrivateMemorySize64;
                bool isImmutable = ProtectedLists.IsImmutableSystemProcess(name) || pid == 4;
                bool isProtected = isImmutable || _userProtectedApps.Contains(name);

                nint hwnd = nint.Zero;
                string? title = null;
                try
                {
                    hwnd = proc.MainWindowHandle;
                    if (hwnd != nint.Zero) title = proc.MainWindowTitle;
                }
                catch { }

                bool isForeground = pid == foregroundPid && foregroundPid > 0;
                if (isForeground)
                {
                    _lastForegroundByGroup[name] = now;
                }

                string? exePath = QueryProcessPath(pid);
                string appName = ResolveFriendlyName(name, exePath);
                DateTimeOffset? startTime = null;
                try { startTime = new DateTimeOffset(proc.StartTime); } catch { }

                double cpuPct = 0;
                ulong ioDeltaBytes = 0;
                ulong currentIoBytes = QueryProcessIoBytes(pid);

                try
                {
                    var totalCpu = proc.TotalProcessorTime;
                    if (_prevSamples.TryGetValue(pid, out var prev))
                    {
                        double elapsedMs = (now - prev.Timestamp).TotalMilliseconds;
                        if (elapsedMs > 50)
                        {
                            double cpuDeltaMs = (totalCpu - prev.CpuTime).TotalMilliseconds;
                            cpuPct = Math.Clamp(cpuDeltaMs / (elapsedMs * _logicalCores) * 100.0, 0.0, 100.0);
                        }
                        if (currentIoBytes >= prev.IoBytes)
                        {
                            ioDeltaBytes = currentIoBytes - prev.IoBytes;
                        }
                    }

                    _prevSamples[pid] = (totalCpu, currentIoBytes, now);
                }
                catch { }

                bool hasActiveWork = cpuPct >= 1.5 || ioDeltaBytes > 64 * 1024 ||
                                     ProtectedLists.ActiveBuildAndWorkProcessNames.Contains(name);

                TimeSpan idleDuration;
                if (isForeground || hasActiveWork)
                {
                    idleDuration = TimeSpan.Zero;
                    _lastForegroundByGroup[name] = now;
                }
                else if (_lastForegroundByGroup.TryGetValue(name, out var lastActive))
                {
                    idleDuration = now - lastActive;
                }
                else
                {
                    _lastForegroundByGroup.TryAdd(name, startTime ?? now);
                    idleDuration = userInputIdle;
                }

                var status = isImmutable
                    ? ProcessActivityStatus.System
                    : isForeground
                        ? ProcessActivityStatus.ActiveForeground
                        : hasActiveWork
                            ? ProcessActivityStatus.ActiveBackgroundWork
                            : isProtected
                                ? ProcessActivityStatus.Protected
                                : ProcessActivityStatus.Idle;

                results.Add(new ProcessInfo(
                    ProcessId: pid,
                    ParentProcessId: parentPid,
                    ProcessName: name,
                    ApplicationName: appName,
                    ExecutablePath: exePath,
                    CpuPercent: Math.Round(cpuPct, 1),
                    WorkingSetBytes: ws,
                    PrivateMemoryBytes: priv,
                    StartTimeUtc: startTime,
                    IsForeground: isForeground,
                    HasVisibleWindow: hwnd != nint.Zero,
                    MainWindowTitle: string.IsNullOrWhiteSpace(title) ? null : title,
                    IdleDuration: idleDuration,
                    ActivityStatus: status,
                    IsProtected: isProtected,
                    IsImmutableSystem: isImmutable,
                    IsEcoQoSActive: _ecoQoSActivePids.ContainsKey(pid)));
            }
            catch
            {
            }
            finally
            {
                proc.Dispose();
            }
        }

        // Cleanup exited PIDs
        foreach (var stalePid in _prevSamples.Keys.Where(id => !activePids.Contains(id)).ToList())
        {
            _prevSamples.TryRemove(stalePid, out _);
            _ecoQoSActivePids.TryRemove(stalePid, out _);
        }

        return results.OrderByDescending(p => p.PrivateMemoryBytes).ToList();
    }

    private async Task EnsureWhitelistLoadedAsync(CancellationToken ct)
    {
        if ((DateTimeOffset.UtcNow - _whitelistLoadedUtc) < TimeSpan.FromSeconds(30) && _userProtectedApps.Count > 0)
            return;

        var saved = await _repository.GetProtectedAppsAsync(ct);
        _userProtectedApps = new HashSet<string>(saved, StringComparer.OrdinalIgnoreCase);
        _whitelistLoadedUtc = DateTimeOffset.UtcNow;
    }

    private static Dictionary<int, int> ReadParentPidMap()
    {
        var map = new Dictionary<int, int>();
        using var snap = ProcessInterop.CreateToolhelp32Snapshot(ProcessInterop.TH32CS_SNAPPROCESS, 0);
        if (snap.IsInvalid) return map;

        var entry = new ProcessInterop.PROCESSENTRY32W
        {
            dwSize = (uint)Marshal.SizeOf<ProcessInterop.PROCESSENTRY32W>()
        };

        if (ProcessInterop.Process32FirstW(snap, ref entry))
        {
            do
            {
                map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            } while (ProcessInterop.Process32NextW(snap, ref entry));
        }

        return map;
    }

    private static unsafe string? QueryProcessPath(int pid)
    {
        using var handle = ProcessInterop.OpenProcess(ProcessInterop.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle.IsInvalid) return null;

        uint size = 1024;
        char* buf = stackalloc char[(int)size];
        return ProcessInterop.QueryFullProcessImageNameW(handle, 0, buf, ref size)
            ? new string(buf, 0, (int)size)
            : null;
    }

    private static ulong QueryProcessIoBytes(int pid)
    {
        using var handle = ProcessInterop.OpenProcess(ProcessInterop.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle.IsInvalid) return 0;

        return ProcessInterop.GetProcessIoCounters(handle, out var io)
            ? io.ReadTransferCount + io.WriteTransferCount
            : 0;
    }

    private static TimeSpan ReadSystemUserIdleTime()
    {
        var info = new ProcessInterop.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<ProcessInterop.LASTINPUTINFO>() };
        if (!ProcessInterop.GetLastInputInfo(ref info)) return TimeSpan.Zero;
        uint elapsedMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(elapsedMs);
    }

    private string ResolveFriendlyName(string processName, string? exePath)
    {
        string known = processName.ToLowerInvariant() switch
        {
            "chrome" => "Google Chrome",
            "msedge" => "Microsoft Edge",
            "code" => "Visual Studio Code",
            "devenv" => "Visual Studio",
            "antigravity" => "Antigravity IDE",
            "sqlservr" => "SQL Server",
            "windowsterminal" => "Windows Terminal",
            "explorer" => "Windows Explorer",
            "svchost" => "Service Host (Windows)",
            "msmpeng" => "Windows Defender",
            "docker desktop" => "Docker Desktop",
            "vmmem" or "vmmemwsl" => "WSL / VM Memory",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(known)) return known;
        if (string.IsNullOrWhiteSpace(exePath)) return processName;

        return _friendlyNameCache.GetOrAdd(exePath, path =>
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName.Trim();
            }
            catch { }
            return processName;
        });
    }

    private Task LogAsync(ActivitySeverity sev, string action, string target, string summary, CancellationToken ct)
        => _repository.LogActivityAsync(new ActivityLogEntry(
            0, DateTimeOffset.UtcNow, sev, "ProcessManager", action, target, summary, false, null), ct);
}

