using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Nexus.Models;

namespace Nexus.Monitoring.Providers;

public sealed class DiskNetworkMonitor : IDisposable
{
    private readonly ILogger _logger;
    private PerformanceCounter? _diskTimeCounter;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private bool _diskCountersInitialized;
    private bool _diskCountersAttempted;

    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTimeOffset _lastNetSampleUtc;
    private bool _netInitialized;

    private readonly string _systemDriveRoot;
    private readonly string _systemDriveLetter;

    public DiskNetworkMonitor(ILogger logger)
    {
        _logger = logger;
        _systemDriveRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        _systemDriveLetter = _systemDriveRoot.TrimEnd('\\');
    }

    public void EnsureDiskCountersInitialized()
    {
        if (_diskCountersAttempted) return;
        _diskCountersAttempted = true;
        TryInitializeDiskCounters();
    }

    private void TryInitializeDiskCounters()
    {
        try
        {
            _diskTimeCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
            _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", readOnly: true);
            _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", readOnly: true);

            // Prime counters once
            _ = _diskTimeCounter.NextValue();
            _ = _diskReadCounter.NextValue();
            _ = _diskWriteCounter.NextValue();
            _diskCountersInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PhysicalDisk performance counters unavailable; continuing with DriveInfo metrics only.");
            _diskCountersInitialized = false;
        }
    }

    public DiskMetrics SampleDisk()
    {
        double activePct = 0;
        long readBps = 0;
        long writeBps = 0;

        if (_diskCountersInitialized)
        {
            try
            {
                activePct = Math.Clamp(Math.Round(_diskTimeCounter!.NextValue(), 1), 0.0, 100.0);
                readBps = Math.Max(0, (long)_diskReadCounter!.NextValue());
                writeBps = Math.Max(0, (long)_diskWriteCounter!.NextValue());
            }
            catch
            {
                activePct = 0;
            }
        }

        long totalBytes = 0;
        long usedBytes = 0;
        try
        {
            var drive = new DriveInfo(_systemDriveRoot);
            if (drive.IsReady)
            {
                totalBytes = drive.TotalSize;
                usedBytes = Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace);
            }
        }
        catch
        {
            // Ignore transient drive query error
        }

        return new DiskMetrics(
            ActiveTimePercent: activePct,
            ReadBytesPerSec: readBps,
            WriteBytesPerSec: writeBps,
            SystemDriveUsedBytes: usedBytes,
            SystemDriveTotalBytes: totalBytes,
            SystemDriveLetter: _systemDriveLetter);
    }

    public NetworkMetrics SampleNetwork()
    {
        long totalRx = 0;
        long totalTx = 0;
        string primaryAdapter = "No Active Adapter";
        long maxAdapterTraffic = -1;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var stats = nic.GetIPStatistics();
                totalRx += stats.BytesReceived;
                totalTx += stats.BytesSent;

                long combined = stats.BytesReceived + stats.BytesSent;
                if (combined > maxAdapterTraffic)
                {
                    maxAdapterTraffic = combined;
                    primaryAdapter = nic.Name;
                }
            }
        }
        catch
        {
            return new NetworkMetrics(0, 0, primaryAdapter);
        }

        var now = DateTimeOffset.UtcNow;
        long downBps = 0;
        long upBps = 0;

        if (_netInitialized)
        {
            double elapsedSec = Math.Max(0.1, (now - _lastNetSampleUtc).TotalSeconds);
            long rxDelta = totalRx - _lastBytesReceived;
            long txDelta = totalTx - _lastBytesSent;

            if (rxDelta >= 0) downBps = (long)(rxDelta / elapsedSec);
            if (txDelta >= 0) upBps = (long)(txDelta / elapsedSec);
        }

        _lastBytesReceived = totalRx;
        _lastBytesSent = totalTx;
        _lastNetSampleUtc = now;
        _netInitialized = true;

        return new NetworkMetrics(downBps, upBps, primaryAdapter);
    }

    public void Dispose()
    {
        _diskTimeCounter?.Dispose();
        _diskReadCounter?.Dispose();
        _diskWriteCounter?.Dispose();
    }
}

