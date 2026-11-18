using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Core.Formatting;
using Nexus.Core.Safety;
using Nexus.Models;
using Nexus.Monitoring;
using Nexus.Monitoring.Providers;
using Nexus.Storage;
using Xunit;

namespace Nexus.Tests;

public sealed class SafetyAndGuardrailTests
{
    private readonly SafetyPolicyValidator _validator = new();

    [Theory]
    [InlineData("WinDefend")]
    [InlineData("RpcSs")]
    [InlineData("PlugPlay")]
    [InlineData("Dhcp")]
    [InlineData("Dnscache")]
    [InlineData("Audiosrv")]
    [InlineData("wuauserv")]
    public void CriticalWindowsServices_AreAlwaysBlockedFromMutation(string serviceName)
    {
        var graph = new ServiceDependencyGraph(serviceName, [], [], HasCriticalDependents: false);
        var result = _validator.ValidateServiceMutation(serviceName, graph, userConfirmed: true);

        Assert.False(result.Succeeded);
        Assert.Equal("CRITICAL_SERVICE_PROTECTED", result.ErrorCode);
    }

    [Fact]
    public void ServiceWithCriticalDependent_IsBlockedEvenIfConfirmed()
    {
        var graph = new ServiceDependencyGraph("CustomBaseSvc", [], ["RpcSs"], HasCriticalDependents: true);
        var result = _validator.ValidateServiceMutation("CustomBaseSvc", graph, userConfirmed: true);

        Assert.False(result.Succeeded);
        Assert.Equal("CRITICAL_DEPENDENT_BLOCKED", result.ErrorCode);
    }

    [Fact]
    public void OptionalService_RequiresExplicitUserConfirmation()
    {
        var graph = new ServiceDependencyGraph("gupdate", [], [], HasCriticalDependents: false);

        var unconfirmed = _validator.ValidateServiceMutation("gupdate", graph, userConfirmed: false);
        Assert.False(unconfirmed.Succeeded);
        Assert.Equal("CONFIRMATION_REQUIRED", unconfirmed.ErrorCode);

        var confirmed = _validator.ValidateServiceMutation("gupdate", graph, userConfirmed: true);
        Assert.True(confirmed.Succeeded);
    }

    [Fact]
    public void ImmutableSystemProcess_CannotBeClosedOrThrottled()
    {
        var proc = new ProcessInfo(
            ProcessId: 4,
            ParentProcessId: 0,
            ProcessName: "lsass",
            ApplicationName: "Local Security Authority",
            ExecutablePath: @"C:\Windows\System32\lsass.exe",
            CpuPercent: 0.1,
            WorkingSetBytes: 20_000_000,
            PrivateMemoryBytes: 15_000_000,
            StartTimeUtc: DateTimeOffset.UtcNow.AddHours(-2),
            IsForeground: false,
            HasVisibleWindow: false,
            MainWindowTitle: null,
            IdleDuration: TimeSpan.FromMinutes(60),
            ActivityStatus: ProcessActivityStatus.System,
            IsProtected: true,
            IsImmutableSystem: true,
            IsEcoQoSActive: false);

        var closeResult = _validator.ValidateProcessAction(proc, SafeActionType.GracefulClose, userConfirmed: true);
        Assert.False(closeResult.Succeeded);
        Assert.Equal("IMMUTABLE_SYSTEM_PROCESS", closeResult.ErrorCode);
    }

    [Fact]
    public void MemoryPressureClassification_Respects75PercentGuardrail()
    {
        Assert.Equal(MemoryPressureLevel.Normal, NativeMemoryMonitor.ClassifyPressure(52.0, 75.0));
        Assert.Equal(MemoryPressureLevel.Elevated, NativeMemoryMonitor.ClassifyPressure(69.0, 75.0));
        Assert.Equal(MemoryPressureLevel.Pressure, NativeMemoryMonitor.ClassifyPressure(75.0, 75.0));
        Assert.Equal(MemoryPressureLevel.Critical, NativeMemoryMonitor.ClassifyPressure(90.0, 75.0));
    }

    [Fact]
    public void ByteFormatter_FormatsValuesAccurately()
    {
        Assert.Equal("1.00 GB", ByteFormatter.FormatBytes(1024L * 1024 * 1024));
        Assert.Equal("512.0 MB/s", ByteFormatter.FormatBytesPerSec(512L * 1024 * 1024));
    }
}

public sealed class StorageAndMonitoringTests
{
    [Fact]
    public async Task SqliteRepository_InitializesSeedsProfilesAndPersistsBackups()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"nexus-test-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteNexusRepository(NullLogger<SqliteNexusRepository>.Instance, tempDb);
            await repo.InitializeAsync();

            var profiles = await repo.GetProfilesAsync();
            Assert.Equal(4, profiles.Count);

            var balanced = Assert.Single(profiles, p => p.Type == ProfileType.Balanced);
            Assert.Equal(75.0, balanced.RamThresholdPercent);
            Assert.Equal(65.0, balanced.CpuThresholdPercent);

            await repo.SetSettingAsync("guardrail.ram", "75");
            Assert.Equal("75", await repo.GetSettingAsync("guardrail.ram"));

            var backup = new ServiceConfigurationSnapshot(
                Guid.NewGuid(),
                "gupdate",
                ServiceStartupType.Automatic,
                OriginalDelayedAutoStart: true,
                ServiceState.Stopped,
                DateTimeOffset.UtcNow,
                "Test snapshot",
                RolledBackUtc: null);

            await repo.SaveServiceBackupAsync(backup);
            var loaded = await repo.GetLatestServiceBackupAsync("gupdate");
            Assert.NotNull(loaded);
            Assert.Equal(backup.Id, loaded.Id);
            Assert.Equal(ServiceStartupType.Automatic, loaded.OriginalStartupType);
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }

    [Fact]
    public void RealHardwareAndTelemetryCapture_ReturnsNonMockWindowsData()
    {
        var hwProvider = new HardwareInfoProvider();
        var hw = hwProvider.GetHardwareInfo();

        Assert.False(string.IsNullOrWhiteSpace(hw.CpuName));
        Assert.True(hw.LogicalProcessors >= 1);
        Assert.True(hw.TotalPhysicalMemoryBytes > 0);
        Assert.True(hw.OsBuildNumber >= 19041);

        var monitor = new SystemMonitorService(hwProvider, NullLogger<SystemMonitorService>.Instance);
        var snap = monitor.CaptureSnapshot();

        Assert.True(snap.Memory.TotalBytes > 0);
        Assert.True(snap.Memory.UsedBytes > 0);
        Assert.Equal(hw.LogicalProcessors, snap.Cpu.PerCoreUsagePercent.Length);
        Assert.True(snap.RunningProcessCount > 10);
        Assert.True(snap.SystemUptime > TimeSpan.Zero);
    }

    [Fact]
    public void TelemetryHistoryBuffer_EnforcesBoundedCapacity()
    {
        var buffer = new TelemetryHistoryBuffer(capacity: 60);
        var hw = new HardwareInfoProvider().GetHardwareInfo();
        var monitor = new SystemMonitorService(new HardwareInfoProvider(), NullLogger<SystemMonitorService>.Instance);
        var sample = monitor.CaptureSnapshot();

        for (int i = 0; i < 80; i++)
        {
            buffer.Add(sample);
        }

        Assert.Equal(60, buffer.Count);
        Assert.Equal(60, buffer.GetWindow(TimeSpan.FromMinutes(5)).Count);
    }
}

