using System.Diagnostics;
using Nexus.Core.Safety;
using Nexus.Models;

namespace Nexus.Monitoring.Providers;

public sealed class TopProcessSampler
{
    private readonly Dictionary<int, (TimeSpan CpuTime, DateTimeOffset Timestamp)> _previousCpu = new();
    private readonly int _logicalCores = Math.Max(1, Environment.ProcessorCount);

    public (int ProcessCount, IReadOnlyList<TopProcessSnapshot> TopProcesses) Sample(int maxCount = 8)
    {
        var now = DateTimeOffset.UtcNow;
        var processes = Process.GetProcesses();
        int totalCount = processes.Length;

        var activePids = new HashSet<int>(totalCount);
        var grouped = new Dictionary<string, (int RepPid, string Name, double Cpu, long Ws, long Priv, int Count)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var proc in processes)
        {
            try
            {
                int pid = proc.Id;
                if (pid <= 4) continue;

                activePids.Add(pid);
                string name = proc.ProcessName;
                long ws = proc.WorkingSet64;
                long priv = proc.PrivateMemorySize64;

                double cpuPct = 0;
                try
                {
                    var totalCpu = proc.TotalProcessorTime;
                    if (_previousCpu.TryGetValue(pid, out var prev))
                    {
                        double elapsedMs = (now - prev.Timestamp).TotalMilliseconds;
                        if (elapsedMs > 50)
                        {
                            double cpuDeltaMs = (totalCpu - prev.CpuTime).TotalMilliseconds;
                            cpuPct = Math.Clamp(cpuDeltaMs / (elapsedMs * _logicalCores) * 100.0, 0.0, 100.0);
                        }
                    }

                    _previousCpu[pid] = (totalCpu, now);
                }
                catch
                {
                    // Protected/elevated process CPU time inaccessible when unelevated
                }

                if (grouped.TryGetValue(name, out var existing))
                {
                    grouped[name] = (
                        existing.RepPid,
                        name,
                        existing.Cpu + cpuPct,
                        existing.Ws + ws,
                        existing.Priv + priv,
                        existing.Count + 1);
                }
                else
                {
                    grouped[name] = (pid, name, cpuPct, ws, priv, 1);
                }
            }
            catch
            {
                // Process exited mid-enumeration or AccessDenied
            }
            finally
            {
                proc.Dispose();
            }
        }

        // Remove exited PIDs to keep memory flat
        var stale = _previousCpu.Keys.Where(pid => !activePids.Contains(pid)).ToList();
        foreach (var pid in stale)
        {
            _previousCpu.Remove(pid);
        }

        var top = grouped.Values
            .OrderByDescending(g => g.Priv)
            .ThenByDescending(g => g.Cpu)
            .Take(maxCount)
            .Select(g => new TopProcessSnapshot(
                ProcessId: g.RepPid,
                ProcessName: g.Name,
                DisplayName: FormatFriendlyName(g.Name),
                CpuPercent: Math.Round(g.Cpu, 1),
                WorkingSetBytes: g.Ws,
                PrivateMemoryBytes: g.Priv,
                InstanceCount: g.Count,
                IsProtected: ProtectedLists.IsImmutableSystemProcess(g.Name) ||
                             ProtectedLists.DefaultProtectedDeveloperApps.Contains(g.Name)))
            .ToList();

        return (totalCount, top);
    }

    private static string FormatFriendlyName(string processName) => processName.ToLowerInvariant() switch
    {
        "chrome" => "Google Chrome",
        "msedge" => "Microsoft Edge",
        "code" => "Visual Studio Code",
        "devenv" => "Visual Studio",
        "antigravity" => "Antigravity IDE",
        "sqlservr" => "SQL Server",
        "windowsterminal" => "Windows Terminal",
        "explorer" => "Windows Explorer",
        "svchost" => "Service Host",
        "msmpeng" => "Windows Defender",
        "vmmem" or "vmmemwsl" => "WSL / VM Memory",
        _ => processName
    };
}

