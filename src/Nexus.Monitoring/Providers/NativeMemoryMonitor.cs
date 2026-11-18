using System.Runtime.InteropServices;
using Nexus.Models;
using Nexus.Monitoring.Native;

namespace Nexus.Monitoring.Providers;

public sealed class NativeMemoryMonitor
{
    public MemoryMetrics Sample(double ramGuardrailPercent)
    {
        var memStatus = new NativeInterop.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeInterop.MEMORYSTATUSEX>()
        };

        long totalBytes = 0;
        long availableBytes = 0;
        if (NativeInterop.GlobalMemoryStatusEx(ref memStatus))
        {
            totalBytes = (long)memStatus.ullTotalPhys;
            availableBytes = (long)memStatus.ullAvailPhys;
        }

        long usedBytes = Math.Max(0, totalBytes - availableBytes);
        double usagePercent = totalBytes > 0
            ? Math.Round((double)usedBytes / totalBytes * 100.0, 1)
            : 0.0;

        long cachedBytes = 0;
        long committedBytes = 0;
        long commitLimitBytes = 0;

        uint perfSize = (uint)Marshal.SizeOf<NativeInterop.PERFORMANCE_INFORMATION>();
        if (NativeInterop.GetPerformanceInfo(out var perfInfo, perfSize))
        {
            long pageSize = (long)perfInfo.PageSize;
            cachedBytes = (long)perfInfo.SystemCache * pageSize;
            committedBytes = (long)perfInfo.CommitTotal * pageSize;
            commitLimitBytes = (long)perfInfo.CommitLimit * pageSize;
        }

        var pressure = ClassifyPressure(usagePercent, ramGuardrailPercent);

        return new MemoryMetrics(
            TotalBytes: totalBytes,
            UsedBytes: usedBytes,
            AvailableBytes: availableBytes,
            CachedBytes: cachedBytes,
            CommittedBytes: committedBytes,
            CommitLimitBytes: commitLimitBytes,
            UsagePercent: usagePercent,
            PressureLevel: pressure,
            IsAboveGuardrail: usagePercent >= ramGuardrailPercent);
    }

    public static MemoryPressureLevel ClassifyPressure(double usagePercent, double guardrailPercent)
    {
        if (usagePercent >= Math.Max(88.0, guardrailPercent + 10.0)) return MemoryPressureLevel.Critical;
        if (usagePercent >= guardrailPercent) return MemoryPressureLevel.Pressure;
        if (usagePercent >= Math.Max(55.0, guardrailPercent - 10.0)) return MemoryPressureLevel.Elevated;
        return MemoryPressureLevel.Normal;
    }
}

