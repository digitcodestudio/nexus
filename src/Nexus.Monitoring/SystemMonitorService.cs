using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using Nexus.Core.Contracts;
using Nexus.Core.Safety;
using Nexus.Models;
using Nexus.Monitoring.Providers;

namespace Nexus.Monitoring;

public sealed class SystemMonitorService : ISystemMonitorService
{
    private readonly ILogger<SystemMonitorService> _logger;
    private readonly NativeCpuMonitor _cpuMonitor = new();
    private readonly NativeMemoryMonitor _memoryMonitor = new();
    private readonly DiskNetworkMonitor _diskNetworkMonitor;
    private readonly TopProcessSampler _topProcessSampler = new();
    private readonly TelemetryHistoryBuffer _historyBuffer = new(capacity: 3600);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private int _tickCount;
    private int _cachedRunningServices;

    public HardwareInfo HardwareInfo { get; }
    public TimeSpan SamplingInterval { get; set; } = TimeSpan.FromMilliseconds(GuardrailDefaults.DefaultSamplingIntervalMs);
    public double RamGuardrailPercent { get; set; } = GuardrailDefaults.DefaultRamThresholdPercent;
    public double CpuGuardrailPercent { get; set; } = GuardrailDefaults.DefaultCpuThresholdPercent;
    public SystemSnapshot? LatestSnapshot { get; private set; }
    public bool IsRunning => _loopTask is { IsCompleted: false };

    public event EventHandler<SystemSnapshot>? SnapshotUpdated;

    public SystemMonitorService(IHardwareInfoProvider hardwareInfoProvider, ILogger<SystemMonitorService> logger)
    {
        _logger = logger;
        HardwareInfo = hardwareInfoProvider.GetHardwareInfo();
        _diskNetworkMonitor = new DiskNetworkMonitor(logger);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("NEXUS SystemMonitorService started with {IntervalMs}ms interval.", SamplingInterval.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts is null) return;

        await _cts.CancelAsync();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public IReadOnlyList<SystemSnapshot> GetHistory(TimeSpan window) => _historyBuffer.GetWindow(window);

    private async Task RunLoopAsync(CancellationToken ct)
    {
        // Prime baseline counters on background thread so UI never blocks
        _ = _cpuMonitor.Sample(CpuGuardrailPercent);
        _ = _diskNetworkMonitor.SampleNetwork();
        _diskNetworkMonitor.EnsureDiskCountersInitialized();
        _cachedRunningServices = CountRunningServicesSafe();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snapshot = CaptureSnapshot();
                LatestSnapshot = snapshot;
                _historyBuffer.Add(snapshot);
                SnapshotUpdated?.Invoke(this, snapshot);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error while capturing system telemetry snapshot.");
            }

            var delay = TimeSpan.FromMilliseconds(Math.Clamp(SamplingInterval.TotalMilliseconds, 500, 10000));
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public SystemSnapshot CaptureSnapshot()
    {
        _tickCount++;
        if (_tickCount % 5 == 0)
        {
            _cachedRunningServices = CountRunningServicesSafe();
        }

        var cpu = _cpuMonitor.Sample(CpuGuardrailPercent, GuardrailDefaults.DefaultSustainedCpuSeconds);
        var memory = _memoryMonitor.Sample(RamGuardrailPercent);
        var disk = _diskNetworkMonitor.SampleDisk();
        var network = _diskNetworkMonitor.SampleNetwork();
        var (procCount, topProcs) = _topProcessSampler.Sample(maxCount: 8);
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        return new SystemSnapshot(
            TimestampUtc: DateTimeOffset.UtcNow,
            Cpu: cpu,
            Memory: memory,
            Disk: disk,
            Network: network,
            RunningProcessCount: procCount,
            RunningServiceCount: _cachedRunningServices,
            SystemUptime: uptime,
            TopProcesses: topProcs);
    }

    private static int CountRunningServicesSafe()
    {
        try
        {
            var services = ServiceController.GetServices();
            int running = 0;
            foreach (var svc in services)
            {
                try
                {
                    if (svc.Status == ServiceControllerStatus.Running)
                    {
                        running++;
                    }
                }
                catch
                {
                }
                finally
                {
                    svc.Dispose();
                }
            }
            return running;
        }
        catch
        {
            return 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _diskNetworkMonitor.Dispose();
    }
}

