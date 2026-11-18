using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Core.Safety;
using Nexus.CpuOptimizer;
using Nexus.IPC;
using Nexus.MemoryOptimizer;
using Nexus.Models;
using Nexus.Monitoring;
using Nexus.Monitoring.Providers;
using Nexus.Optimizer;
using Nexus.ProcessManager;
using Nexus.ServiceManager;
using Nexus.Storage;
using Xunit;

namespace Nexus.Tests;

public sealed class Phase3To6ModuleTests
{
    [Fact]
    public void ServiceClassifier_ProtectsCriticalAndIdentifiesChromeUpdaterCandidate()
    {
        var (defCat, defRec, defCrit) = ServiceClassifier.Classify("WinDefend", "Microsoft Defender Antivirus", @"C:\ProgramData\...\MsMpEng.exe", ServiceStartupType.Automatic, false);
        Assert.Equal(ServiceCategory.Security, defCat);
        Assert.Equal(ServiceRecommendation.KeepProtected, defRec);
        Assert.True(defCrit);

        var (chrCat, chrRec, chrCrit) = ServiceClassifier.Classify("gupdate", "Google Update Service (gupdate)", @"C:\Program Files (x86)\Google\Update\GoogleUpdate.exe", ServiceStartupType.Automatic, false);
        Assert.Equal(ServiceCategory.OptimizationCandidate, chrCat);
        Assert.Equal(ServiceRecommendation.OptInOptimizationCandidate, chrRec);
        Assert.False(chrCrit);
    }

    [Fact]
    public void ProcessGrouper_GroupsMultipleChromeProcessesCleanly()
    {
        var now = DateTimeOffset.UtcNow;
        var procs = new List<ProcessInfo>
        {
            new(1001, 500, "chrome", "Google Chrome", @"C:\...\chrome.exe", 0.4, 300_000_000, 250_000_000, now.AddHours(-1), false, true, "Tab", TimeSpan.FromMinutes(40), ProcessActivityStatus.Idle, false, false, false),
            new(1002, 1001, "chrome", "Google Chrome", @"C:\...\chrome.exe", 0.2, 200_000_000, 180_000_000, now.AddHours(-1), false, false, null, TimeSpan.FromMinutes(40), ProcessActivityStatus.Idle, false, false, false),
            new(2001, 500, "Code", "Visual Studio Code", @"C:\...\Code.exe", 4.5, 500_000_000, 400_000_000, now.AddHours(-2), true, true, "Nexus", TimeSpan.Zero, ProcessActivityStatus.ActiveForeground, true, false, false)
        };

        var groups = ProcessManagerService.GroupProcesses(procs);
        Assert.Equal(2, groups.Count);

        var chromeGroup = Assert.Single(groups, g => g.GroupKey == "chrome");
        Assert.Equal(2, chromeGroup.ProcessCount);
        Assert.Equal(430_000_000, chromeGroup.TotalPrivateMemoryBytes);
        Assert.False(chromeGroup.IsAnyForeground);
        Assert.False(chromeGroup.HasActiveBackgroundWork);

        var codeGroup = Assert.Single(groups, g => g.GroupKey == "Code");
        Assert.True(codeGroup.IsAnyForeground);
        Assert.True(codeGroup.IsProtected);
    }

    [Fact]
    public void MemoryAndCpuOptimizers_Detect75RamAnd65CpuGuardrailBreaches()
    {
        var now = DateTimeOffset.UtcNow;
        var snap = new SystemSnapshot(
            now,
            new CpuMetrics(72.5, [70, 75], 3600, null, IsSustainedHighLoad: true, IsAboveGuardrail: true),
            new MemoryMetrics(16L * 1024 * 1024 * 1024, 13L * 1024 * 1024 * 1024, 3L * 1024 * 1024 * 1024, 1024, 14L * 1024 * 1024 * 1024, 20L * 1024 * 1024 * 1024, 81.2, MemoryPressureLevel.Pressure, true),
            new DiskMetrics(10, 0, 0, 100, 500, "C:"),
            new NetworkMetrics(0, 0, "Wi-Fi"),
            150, 80, TimeSpan.FromHours(3), []);

        var chromeProc = new ProcessInfo(1001, 500, "chrome", "Google Chrome", null, 12.0, 900_000_000, 850_000_000, now.AddHours(-1), false, false, null, TimeSpan.FromMinutes(35), ProcessActivityStatus.Idle, false, false, false);
        var groups = ProcessManagerService.GroupProcesses([chromeProc]);

        var memOpt = new MemoryOptimizerService(NullLogger<MemoryOptimizerService>.Instance);
        var memReport = memOpt.Analyze(snap, groups, ramThresholdPercent: 75.0);
        Assert.True(memReport.IsGuardrailExceeded);
        Assert.NotEmpty(memReport.Recommendations);

        var cpuOpt = new CpuOptimizerService(NullLogger<CpuOptimizerService>.Instance);
        var cpuReport = cpuOpt.Analyze(snap, groups, cpuThresholdPercent: 65.0);
        Assert.True(cpuReport.IsGuardrailExceeded);
        Assert.NotEmpty(cpuReport.Recommendations);
    }

    [Fact]
    public async Task OptimizationEngine_SwitchesProfilesAndExecutesEmergencyStop()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"nexus-opt-test-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteNexusRepository(NullLogger<SqliteNexusRepository>.Instance, tempDb);
            var hw = new HardwareInfoProvider();
            var monitor = new SystemMonitorService(hw, NullLogger<SystemMonitorService>.Instance);
            var validator = new SafetyPolicyValidator();
            var svcMgr = new WindowsServiceManager(validator, repo, NullLogger<WindowsServiceManager>.Instance);
            var procMgr = new ProcessManagerService(validator, repo, NullLogger<ProcessManagerService>.Instance);
            var trayHygiene = new TrayHygieneService(repo, NullLogger<TrayHygieneService>.Instance);
            var memOpt = new MemoryOptimizerService(NullLogger<MemoryOptimizerService>.Instance);
            var cpuOpt = new CpuOptimizerService(NullLogger<CpuOptimizerService>.Instance);

            var engine = new OptimizationEngine(monitor, procMgr, svcMgr, memOpt, cpuOpt, trayHygiene, repo, NullLogger<OptimizationEngine>.Instance);
            await engine.InitializeAsync();

            await engine.SetActiveProfileAsync(ProfileType.Development);
            Assert.Equal(ProfileType.Development, engine.ActiveProfile.Type);

            await engine.EmergencyStopAsync();
            Assert.True(engine.IsAutomationPaused);
            Assert.True(engine.IsSafeModeEnabled);

            var logs = await repo.GetActivityHistoryAsync(10);
            Assert.Contains(logs, l => l.Action == "EmergencyStop");
        }
        finally
        {
            if (File.Exists(tempDb)) { try { File.Delete(tempDb); } catch { } }
        }
    }

    [Fact]
    public async Task NamedPipeIpc_RoundTripSucceedsWithRestrictedAcl()
    {
        string pipeName = $"NEXUS.Test.Pipe.{Guid.NewGuid():N}";
        await using var server = new NexusIpcServer(
            (req, _) => Task.FromResult(new IpcMessageEnvelope(req.Id, 1, "Pong", DateTimeOffset.UtcNow, "{\"ok\":true}")),
            NullLogger<NexusIpcServer>.Instance,
            pipeName);

        server.Start();
        await Task.Delay(150);

        var client = new NexusIpcClient(pipeName);
        var resp = await client.SendAsync("Ping");

        Assert.NotNull(resp);
        Assert.Equal("Pong", resp.Command);
        Assert.Contains("true", resp.PayloadJson);
    }
}

