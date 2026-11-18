using System.Runtime.InteropServices;
using Nexus.Models;
using Nexus.Monitoring.Native;

namespace Nexus.Monitoring.Providers;

public sealed class NativeCpuMonitor
{
    private readonly int _logicalCoreCount = Math.Max(1, Environment.ProcessorCount);
    private NativeInterop.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[]? _previousSamples;
    private readonly Queue<(DateTimeOffset Timestamp, double Usage)> _recentWindow = new();

    public unsafe CpuMetrics Sample(double cpuGuardrailPercent, int sustainedSeconds = 10)
    {
        var current = new NativeInterop.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[_logicalCoreCount];
        int structSize = Marshal.SizeOf<NativeInterop.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
        int bufferSize = structSize * _logicalCoreCount;

        int status;
        fixed (NativeInterop.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION* ptr = current)
        {
            status = NativeInterop.NtQuerySystemInformation(
                NativeInterop.SystemProcessorPerformanceInformation,
                ptr,
                bufferSize,
                out _);
        }

        var perCore = new double[_logicalCoreCount];
        double totalUsage = 0;

        if (status == 0 && _previousSamples is not null)
        {
            double sum = 0;
            for (int i = 0; i < _logicalCoreCount; i++)
            {
                long idleDelta = current[i].IdleTime - _previousSamples[i].IdleTime;
                long kernelDelta = current[i].KernelTime - _previousSamples[i].KernelTime;
                long userDelta = current[i].UserTime - _previousSamples[i].UserTime;

                // KernelTime includes IdleTime in Windows NT kernel accounting
                long totalDelta = kernelDelta + userDelta;
                double coreUsage = totalDelta > 0
                    ? Math.Clamp((double)(totalDelta - idleDelta) / totalDelta * 100.0, 0.0, 100.0)
                    : 0.0;

                perCore[i] = Math.Round(coreUsage, 1);
                sum += coreUsage;
            }

            totalUsage = Math.Round(sum / _logicalCoreCount, 1);
        }

        if (status == 0)
        {
            _previousSamples = current;
        }

        double? freqMhz = ReadAverageFrequencyMhz();

        var now = DateTimeOffset.UtcNow;
        _recentWindow.Enqueue((now, totalUsage));
        var cutoff = now.AddSeconds(-Math.Max(3, sustainedSeconds));
        while (_recentWindow.Count > 0 && _recentWindow.Peek().Timestamp < cutoff)
        {
            _recentWindow.Dequeue();
        }

        bool isSustained = _recentWindow.Count >= 3 && _recentWindow.Average(x => x.Usage) >= cpuGuardrailPercent;

        return new CpuMetrics(
            TotalUsagePercent: totalUsage,
            PerCoreUsagePercent: perCore,
            CurrentFrequencyMhz: freqMhz,
            TemperatureCelsius: null, // Non-invasive sensor reading only when explicitly supported
            IsSustainedHighLoad: isSustained,
            IsAboveGuardrail: totalUsage >= cpuGuardrailPercent);
    }

    private unsafe double? ReadAverageFrequencyMhz()
    {
        try
        {
            var info = new NativeInterop.PROCESSOR_POWER_INFORMATION[_logicalCoreCount];
            uint size = (uint)(Marshal.SizeOf<NativeInterop.PROCESSOR_POWER_INFORMATION>() * _logicalCoreCount);
            fixed (NativeInterop.PROCESSOR_POWER_INFORMATION* ptr = info)
            {
                uint result = NativeInterop.CallNtPowerInformation(
                    NativeInterop.ProcessorInformationLevel,
                    null,
                    0,
                    ptr,
                    size);

                if (result != 0) return null;
            }

            double avg = info.Average(x => (double)x.CurrentMhz);
            return avg > 0 ? Math.Round(avg, 0) : null;
        }
        catch
        {
            return null;
        }
    }
}

