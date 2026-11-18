using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using Nexus.Core.Contracts;
using Nexus.Core.Safety;
using Nexus.Models;
using Nexus.ServiceManager.Native;

namespace Nexus.ServiceManager;

public sealed class WindowsServiceManager : IWindowsServiceManager
{
    private readonly ISafetyPolicyValidator _safetyValidator;
    private readonly INexusRepository _repository;
    private readonly ILogger<WindowsServiceManager> _logger;

    public WindowsServiceManager(
        ISafetyPolicyValidator safetyValidator,
        INexusRepository repository,
        ILogger<WindowsServiceManager> logger)
    {
        _safetyValidator = safetyValidator;
        _repository = repository;
        _logger = logger;
    }

    public Task<IReadOnlyList<WindowsServiceInfo>> GetAllServicesAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<WindowsServiceInfo>>(() => EnumerateAllServices(ct), ct);

    public Task<WindowsServiceInfo?> GetServiceDetailsAsync(string serviceName, CancellationToken ct = default)
        => Task.Run(() => EnumerateAllServices(ct).FirstOrDefault(s =>
            s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase)), ct);

    public Task<ServiceDependencyGraph> GetDependencyGraphAsync(string serviceName, CancellationToken ct = default)
        => Task.Run(() => BuildDependencyGraph(serviceName), ct);

    public async Task<OperationResult> StartServiceAsync(string serviceName, CancellationToken ct = default)
    {
        try
        {
            await Task.Run(() =>
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running) return;
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }, ct);

            await LogAsync(ActivitySeverity.Success, "StartService", serviceName, $"Layanan '{serviceName}' berhasil dijalankan.", false, null, ct);
            return OperationResult.Ok($"Layanan '{serviceName}' berhasil dijalankan.");
        }
        catch (Exception ex)
        {
            return HandleServiceException("menjalankan", serviceName, ex);
        }
    }

    public async Task<OperationResult> StopServiceAsync(string serviceName, bool userConfirmed, CancellationToken ct = default)
    {
        var graph = await GetDependencyGraphAsync(serviceName, ct);
        var validation = _safetyValidator.ValidateServiceMutation(serviceName, graph, userConfirmed);
        if (!validation.Succeeded) return validation;

        var details = await GetServiceDetailsAsync(serviceName, ct);
        if (details is null)
            return OperationResult.Fail($"Layanan '{serviceName}' tidak ditemukan.", "NOT_FOUND");

        var backup = new ServiceConfigurationSnapshot(
            Guid.NewGuid(),
            serviceName,
            details.StartupType,
            details.StartupType == ServiceStartupType.AutomaticDelayedStart,
            details.State,
            DateTimeOffset.UtcNow,
            "Snapshot sebelum menghentikan layanan",
            null);

        await _repository.SaveServiceBackupAsync(backup, ct);

        try
        {
            await Task.Run(() =>
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped) return;
                if (!sc.CanStop)
                    throw new InvalidOperationException($"Layanan '{serviceName}' tidak mendukung perintah henti (CanStop = false).");
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }, ct);

            await LogAsync(ActivitySeverity.Success, "StopService", serviceName,
                $"Layanan '{serviceName}' dihentikan dengan aman (snapshot rollback tersimpan).", true, backup.Id, ct);
            return OperationResult.Ok($"Layanan '{serviceName}' berhasil dihentikan. Snapshot rollback tersimpan.", backup.Id);
        }
        catch (Exception ex)
        {
            return HandleServiceException("menghentikan", serviceName, ex);
        }
    }

    public async Task<OperationResult> RestartServiceAsync(string serviceName, bool userConfirmed, CancellationToken ct = default)
    {
        var graph = await GetDependencyGraphAsync(serviceName, ct);
        var validation = _safetyValidator.ValidateServiceMutation(serviceName, graph, userConfirmed);
        if (!validation.Succeeded) return validation;

        try
        {
            await Task.Run(() =>
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }, ct);

            await LogAsync(ActivitySeverity.Success, "RestartService", serviceName, $"Layanan '{serviceName}' berhasil dimulai ulang.", false, null, ct);
            return OperationResult.Ok($"Layanan '{serviceName}' berhasil dimulai ulang.");
        }
        catch (Exception ex)
        {
            return HandleServiceException("memulai ulang", serviceName, ex);
        }
    }

    public async Task<OperationResult> ChangeStartupTypeAsync(
        string serviceName,
        ServiceStartupType newType,
        bool userConfirmed,
        CancellationToken ct = default)
    {
        var graph = await GetDependencyGraphAsync(serviceName, ct);
        var validation = _safetyValidator.ValidateServiceMutation(serviceName, graph, userConfirmed);
        if (!validation.Succeeded) return validation;

        var details = await GetServiceDetailsAsync(serviceName, ct);
        if (details is null)
            return OperationResult.Fail($"Layanan '{serviceName}' tidak ditemukan.", "NOT_FOUND");

        var backup = new ServiceConfigurationSnapshot(
            Guid.NewGuid(),
            serviceName,
            details.StartupType,
            details.StartupType == ServiceStartupType.AutomaticDelayedStart,
            details.State,
            DateTimeOffset.UtcNow,
            $"Snapshot sebelum ubah startup menjadi {newType}",
            null);

        await _repository.SaveServiceBackupAsync(backup, ct);

        var applyResult = await Task.Run(() => ApplyNativeStartupType(serviceName, newType), ct);
        if (!applyResult.Succeeded) return applyResult;

        await LogAsync(ActivitySeverity.Success, "ChangeStartupType", serviceName,
            $"Tipe startup '{serviceName}' diubah dari {details.StartupType} ke {newType}.", true, backup.Id, ct);
        return OperationResult.Ok($"Tipe startup '{serviceName}' diubah ke {newType}. Snapshot rollback tersimpan.", backup.Id);
    }

    public async Task<OperationResult> RollbackServiceAsync(string serviceName, CancellationToken ct = default)
    {
        var backup = await _repository.GetLatestServiceBackupAsync(serviceName, ct);
        if (backup is null)
            return OperationResult.Fail($"Tidak ada snapshot rollback aktif untuk layanan '{serviceName}'.", "NO_BACKUP");

        var applyResult = await Task.Run(() => ApplyNativeStartupType(serviceName, backup.OriginalStartupType), ct);
        if (!applyResult.Succeeded) return applyResult;

        if (backup.OriginalState == ServiceState.Running)
        {
            await StartServiceAsync(serviceName, ct);
        }

        await _repository.MarkServiceBackupRolledBackAsync(backup.Id, DateTimeOffset.UtcNow, ct);
        await LogAsync(ActivitySeverity.Info, "RollbackService", serviceName,
            $"Konfigurasi layanan '{serviceName}' dikembalikan ke {backup.OriginalStartupType} ({backup.OriginalState}).", false, backup.Id, ct);

        return OperationResult.Ok($"Rollback '{serviceName}' berhasil dikembalikan ke kondisi awal ({backup.OriginalStartupType}).");
    }

    public Task<IReadOnlyList<ServiceConfigurationSnapshot>> GetBackupsAsync(CancellationToken ct = default)
        => _repository.GetAllServiceBackupsAsync(ct);

    private IReadOnlyList<WindowsServiceInfo> EnumerateAllServices(CancellationToken ct)
    {
        var result = new List<WindowsServiceInfo>();
        using var scm = ScmInterop.OpenSCManagerW(null, null, ScmInterop.SC_MANAGER_CONNECT | ScmInterop.SC_MANAGER_ENUMERATE_SERVICE);

        ServiceController[] controllers;
        try
        {
            controllers = ServiceController.GetServices();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal membaca daftar layanan Windows.");
            return result;
        }

        foreach (var sc in controllers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string name = sc.ServiceName;
                string displayName = sc.DisplayName;
                var state = MapState(sc.Status);

                var deps = SafeReadNames(() => sc.ServicesDependedOn);
                var dependents = SafeReadNames(() => sc.DependentServices);

                ServiceStartupType startupType = ServiceStartupType.Unknown;
                string description = string.Empty;
                string? binaryPath = null;
                string? group = null;
                int? pid = null;
                bool isTrigger = false;

                if (!scm.IsInvalid)
                {
                    using var svc = ScmInterop.OpenServiceW(scm, name, ScmInterop.SERVICE_QUERY_CONFIG | ScmInterop.SERVICE_QUERY_STATUS);
                    if (!svc.IsInvalid)
                    {
                        (startupType, binaryPath, group) = QueryBasicConfig(svc);
                        if (startupType == ServiceStartupType.Automatic && QueryDelayedAutoStart(svc))
                        {
                            startupType = ServiceStartupType.AutomaticDelayedStart;
                        }
                        description = QueryDescription(svc);
                        isTrigger = QueryIsTriggerStart(svc);
                        pid = QueryProcessId(svc);
                    }
                }

                if (startupType == ServiceStartupType.Unknown)
                {
                    try { startupType = MapStartMode(sc.StartType); } catch { }
                }

                var (category, recommendation, isCritical) = ServiceClassifier.Classify(
                    name, displayName, binaryPath, startupType, isTrigger);

                result.Add(new WindowsServiceInfo(
                    ServiceName: name,
                    DisplayName: displayName,
                    Description: string.IsNullOrWhiteSpace(description) ? "Tidak ada deskripsi layanan." : description,
                    State: state,
                    StartupType: startupType,
                    ProcessId: pid,
                    BinaryPath: binaryPath,
                    IsTriggerStart: isTrigger,
                    ServiceGroup: group,
                    Dependencies: deps,
                    DependentServices: dependents,
                    Category: category,
                    Recommendation: recommendation,
                    IsCriticalProtected: isCritical,
                    LastKnownActivityUtc: state == ServiceState.Running ? DateTimeOffset.UtcNow : null));
            }
            catch
            {
                // Skip individual inaccessible service cleanly
            }
            finally
            {
                sc.Dispose();
            }
        }

        return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ServiceDependencyGraph BuildDependencyGraph(string serviceName)
    {
        var directDeps = new List<string>();
        var allDependents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var root = new ServiceController(serviceName);
            directDeps.AddRange(SafeReadNames(() => root.ServicesDependedOn));

            var queue = new Queue<string>(SafeReadNames(() => root.DependentServices));
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!allDependents.Add(current)) continue;

                try
                {
                    using var child = new ServiceController(current);
                    foreach (var next in SafeReadNames(() => child.DependentServices))
                    {
                        if (!allDependents.Contains(next)) queue.Enqueue(next);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        bool hasCritical = allDependents.Any(ProtectedLists.IsImmutableCriticalService);
        return new ServiceDependencyGraph(serviceName, directDeps, allDependents.ToList(), hasCritical);
    }

    private OperationResult ApplyNativeStartupType(string serviceName, ServiceStartupType newType)
    {
        uint nativeStart = newType switch
        {
            ServiceStartupType.Automatic or ServiceStartupType.AutomaticDelayedStart => ScmInterop.SERVICE_AUTO_START,
            ServiceStartupType.Manual => ScmInterop.SERVICE_DEMAND_START,
            ServiceStartupType.Disabled => ScmInterop.SERVICE_DISABLED,
            _ => ScmInterop.SERVICE_DEMAND_START
        };

        using var scm = ScmInterop.OpenSCManagerW(null, null, ScmInterop.SC_MANAGER_CONNECT);
        if (scm.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            return OperationResult.Fail(
                "Gagal membuka Service Control Manager. Jalankan NEXUS sebagai Administrator untuk mengubah layanan Windows.",
                $"WIN32_{err}",
                requiresElevation: err == 5);
        }

        using var svc = ScmInterop.OpenServiceW(scm, serviceName, ScmInterop.SERVICE_QUERY_CONFIG | ScmInterop.SERVICE_CHANGE_CONFIG);
        if (svc.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            return OperationResult.Fail(
                err == 5
                    ? $"Akses ditolak untuk mengubah '{serviceName}'. Diperlukan hak Administrator (UAC) atau layanan dilindungi Windows."
                    : $"Gagal membuka layanan '{serviceName}' (Kode Win32: {err}).",
                $"WIN32_{err}",
                requiresElevation: err == 5);
        }

        bool ok = ScmInterop.ChangeServiceConfigW(
            svc,
            ScmInterop.SERVICE_NO_CHANGE,
            nativeStart,
            ScmInterop.SERVICE_NO_CHANGE,
            null, null, nint.Zero, null, null, null, null);

        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            return OperationResult.Fail(
                $"Gagal mengubah tipe startup '{serviceName}' (Kode Win32: {err}).",
                $"WIN32_{err}",
                requiresElevation: err == 5);
        }

        if (newType is ServiceStartupType.Automatic or ServiceStartupType.AutomaticDelayedStart)
        {
            var delayed = new ScmInterop.SERVICE_DELAYED_AUTO_START_INFO
            {
                fDelayedAutostart = newType == ServiceStartupType.AutomaticDelayedStart ? 1 : 0
            };
            ScmInterop.ChangeServiceConfig2W(svc, ScmInterop.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, ref delayed);
        }

        return OperationResult.Ok("OK");
    }

    private static (ServiceStartupType, string?, string?) QueryBasicConfig(SafeServiceHandle svc)
    {
        ScmInterop.QueryServiceConfigW(svc, nint.Zero, 0, out uint needed);
        if (needed == 0) return (ServiceStartupType.Unknown, null, null);

        nint buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ScmInterop.QueryServiceConfigW(svc, buffer, needed, out _))
                return (ServiceStartupType.Unknown, null, null);

            var cfg = Marshal.PtrToStructure<ScmInterop.QUERY_SERVICE_CONFIG>(buffer);
            var startType = cfg.dwStartType switch
            {
                ScmInterop.SERVICE_BOOT_START => ServiceStartupType.Boot,
                ScmInterop.SERVICE_SYSTEM_START => ServiceStartupType.System,
                ScmInterop.SERVICE_AUTO_START => ServiceStartupType.Automatic,
                ScmInterop.SERVICE_DEMAND_START => ServiceStartupType.Manual,
                ScmInterop.SERVICE_DISABLED => ServiceStartupType.Disabled,
                _ => ServiceStartupType.Unknown
            };
            string? bin = cfg.lpBinaryPathName != nint.Zero ? Marshal.PtrToStringUni(cfg.lpBinaryPathName) : null;
            string? grp = cfg.lpLoadOrderGroup != nint.Zero ? Marshal.PtrToStringUni(cfg.lpLoadOrderGroup) : null;
            return (startType, bin, string.IsNullOrWhiteSpace(grp) ? null : grp);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool QueryDelayedAutoStart(SafeServiceHandle svc)
    {
        int size = Marshal.SizeOf<ScmInterop.SERVICE_DELAYED_AUTO_START_INFO>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!ScmInterop.QueryServiceConfig2W(svc, ScmInterop.SERVICE_CONFIG_DELAYED_AUTO_START_INFO, buffer, (uint)size, out _))
                return false;
            return Marshal.PtrToStructure<ScmInterop.SERVICE_DELAYED_AUTO_START_INFO>(buffer).fDelayedAutostart != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string QueryDescription(SafeServiceHandle svc)
    {
        ScmInterop.QueryServiceConfig2W(svc, ScmInterop.SERVICE_CONFIG_DESCRIPTION, nint.Zero, 0, out uint needed);
        if (needed == 0) return string.Empty;

        nint buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ScmInterop.QueryServiceConfig2W(svc, ScmInterop.SERVICE_CONFIG_DESCRIPTION, buffer, needed, out _))
                return string.Empty;
            nint strPtr = Marshal.ReadIntPtr(buffer);
            return strPtr != nint.Zero ? Marshal.PtrToStringUni(strPtr) ?? string.Empty : string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool QueryIsTriggerStart(SafeServiceHandle svc)
    {
        ScmInterop.QueryServiceConfig2W(svc, ScmInterop.SERVICE_CONFIG_TRIGGER_INFO, nint.Zero, 0, out uint needed);
        if (needed < 4) return false;

        nint buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ScmInterop.QueryServiceConfig2W(svc, ScmInterop.SERVICE_CONFIG_TRIGGER_INFO, buffer, needed, out _))
                return false;
            return Marshal.ReadInt32(buffer) > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int? QueryProcessId(SafeServiceHandle svc)
    {
        int size = Marshal.SizeOf<ScmInterop.SERVICE_STATUS_PROCESS>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!ScmInterop.QueryServiceStatusEx(svc, ScmInterop.SC_STATUS_PROCESS_INFO, buffer, (uint)size, out _))
                return null;
            var status = Marshal.PtrToStructure<ScmInterop.SERVICE_STATUS_PROCESS>(buffer);
            return status.dwProcessId > 0 ? (int)status.dwProcessId : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static List<string> SafeReadNames(Func<ServiceController[]> selector)
    {
        try
        {
            var items = selector();
            var names = new List<string>(items.Length);
            foreach (var item in items)
            {
                names.Add(item.ServiceName);
                item.Dispose();
            }
            return names;
        }
        catch
        {
            return [];
        }
    }

    private static ServiceState MapState(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServiceState.Running,
        ServiceControllerStatus.Stopped => ServiceState.Stopped,
        ServiceControllerStatus.StartPending => ServiceState.StartPending,
        ServiceControllerStatus.StopPending => ServiceState.StopPending,
        ServiceControllerStatus.Paused => ServiceState.Paused,
        ServiceControllerStatus.ContinuePending => ServiceState.ContinuePending,
        ServiceControllerStatus.PausePending => ServiceState.PausePending,
        _ => ServiceState.Unknown
    };

    private static ServiceStartupType MapStartMode(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => ServiceStartupType.Automatic,
        ServiceStartMode.Manual => ServiceStartupType.Manual,
        ServiceStartMode.Disabled => ServiceStartupType.Disabled,
        ServiceStartMode.Boot => ServiceStartupType.Boot,
        ServiceStartMode.System => ServiceStartupType.System,
        _ => ServiceStartupType.Unknown
    };

    private OperationResult HandleServiceException(string verb, string serviceName, Exception ex)
    {
        _logger.LogWarning(ex, "Gagal {Verb} layanan {ServiceName}.", verb, serviceName);
        bool accessDenied = ex is UnauthorizedAccessException ||
                            (ex is Win32Exception w32 && w32.NativeErrorCode == 5) ||
                            (ex.InnerException is Win32Exception iw32 && iw32.NativeErrorCode == 5);

        return accessDenied
            ? OperationResult.Fail(
                $"Akses ditolak saat {verb} '{serviceName}'. Diperlukan hak Administrator (Jalankan sebagai Admin).",
                "ACCESS_DENIED",
                requiresElevation: true)
            : OperationResult.Fail($"Gagal {verb} layanan '{serviceName}': {ex.Message}", "SERVICE_ERROR");
    }

    private Task LogAsync(ActivitySeverity sev, string action, string target, string summary, bool reversible, Guid? rollbackId, CancellationToken ct)
        => _repository.LogActivityAsync(new ActivityLogEntry(
            0, DateTimeOffset.UtcNow, sev, "ServiceManager", action, target, summary, reversible, rollbackId), ct);
}

