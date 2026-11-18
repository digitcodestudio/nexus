using System.Runtime.InteropServices;
using Microsoft.Win32;
using Nexus.Core.Contracts;
using Nexus.Models;
using Nexus.Monitoring.Native;

namespace Nexus.Monitoring.Providers;

public sealed class HardwareInfoProvider : IHardwareInfoProvider
{
    private HardwareInfo? _cached;

    public HardwareInfo GetHardwareInfo()
    {
        if (_cached is not null) return _cached;

        string cpuName = ReadRegistryString(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString")?.Trim() ?? "Windows Processor";

        int logicalProcessors = Environment.ProcessorCount;
        int physicalCores = Math.Max(1, logicalProcessors / 2);

        long totalPhysicalMemory = 0;
        var memStatus = new NativeInterop.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeInterop.MEMORYSTATUSEX>()
        };
        if (NativeInterop.GlobalMemoryStatusEx(ref memStatus))
        {
            totalPhysicalMemory = (long)memStatus.ullTotalPhys;
        }

        string productName = ReadRegistryString(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "ProductName") ?? "Windows";
        string displayVersion = ReadRegistryString(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "DisplayVersion") ?? Environment.OSVersion.Version.ToString();
        string buildStr = ReadRegistryString(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "CurrentBuildNumber") ?? Environment.OSVersion.Version.Build.ToString();

        int buildNumber = int.TryParse(buildStr, out var parsedBuild)
            ? parsedBuild
            : Environment.OSVersion.Version.Build;

        // Windows 11 reports "Windows 10 ..." in ProductName registry key on some builds >= 22000
        if (buildNumber >= 22000 && productName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            productName = productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        _cached = new HardwareInfo(
            CpuName: cpuName,
            PhysicalCores: physicalCores,
            LogicalProcessors: logicalProcessors,
            TotalPhysicalMemoryBytes: totalPhysicalMemory,
            WindowsEdition: productName,
            WindowsVersion: displayVersion,
            OsBuildNumber: buildNumber,
            Architecture: RuntimeInformation.OSArchitecture.ToString());

        return _cached;
    }

    private static string? ReadRegistryString(string subKey, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
            return key?.GetValue(valueName)?.ToString();
        }
        catch
        {
            return null;
        }
    }
}

