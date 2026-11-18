using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.Core.Contracts;
using Nexus.Core.Safety;
using Nexus.CpuOptimizer;
using Nexus.IPC;
using Nexus.MemoryOptimizer;
using Nexus.Monitoring;
using Nexus.Monitoring.Providers;
using Nexus.Optimizer;
using Nexus.ProcessManager;
using Nexus.ServiceManager;
using Nexus.Storage;

namespace Nexus.BackgroundService;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "NEXUS System Optimizer Engine";
        });

        builder.Services.AddSingleton<ISafetyPolicyValidator, SafetyPolicyValidator>();
        builder.Services.AddSingleton<INexusRepository, SqliteNexusRepository>();
        builder.Services.AddSingleton<IHardwareInfoProvider, HardwareInfoProvider>();
        builder.Services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        builder.Services.AddSingleton<IWindowsServiceManager, WindowsServiceManager>();
        builder.Services.AddSingleton<IProcessManagerService, ProcessManagerService>();
        builder.Services.AddSingleton<TrayHygieneService>();
        builder.Services.AddSingleton<IMemoryOptimizer, MemoryOptimizerService>();
        builder.Services.AddSingleton<ICpuOptimizer, CpuOptimizerService>();
        builder.Services.AddSingleton<OptimizationEngine>();
        builder.Services.AddSingleton<IOptimizationEngine>(sp => sp.GetRequiredService<OptimizationEngine>());
        builder.Services.AddHostedService<NexusWorker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}

public sealed class NexusWorker(
    ISystemMonitorService monitor,
    OptimizationEngine optimizer,
    INexusRepository repository,
    ILogger<NexusWorker> logger,
    ILogger<NexusIpcServer> ipcLogger) : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await repository.InitializeAsync(stoppingToken);
        await optimizer.InitializeAsync(stoppingToken);
        await monitor.StartAsync(stoppingToken);

        await using var ipcServer = new NexusIpcServer(HandleIpcCommandAsync, ipcLogger);
        ipcServer.Start(stoppingToken);

        logger.LogInformation("NEXUS Background Service berjalan (Profil: {Profile}, RAM <= {Ram}%, CPU <= {Cpu}%).",
            optimizer.ActiveProfile.Name, optimizer.ActiveProfile.RamThresholdPercent, optimizer.ActiveProfile.CpuThresholdPercent);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        int tick = 0;
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            tick++;
            try
            {
                if (monitor.LatestSnapshot is not null && tick % 4 == 0)
                {
                    await repository.AppendMonitoringSampleAsync(monitor.LatestSnapshot, stoppingToken);
                }

                await optimizer.EvaluateRecommendationsAsync(stoppingToken);

                if (tick % 240 == 0)
                {
                    await repository.PruneMonitoringHistoryAsync(GuardrailDefaults.DefaultHistoryRetentionDays, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Kesalahan siklus evaluasi latar belakang NEXUS.");
            }
        }

        await monitor.StopAsync(CancellationToken.None);
    }

    private async Task<IpcMessageEnvelope> HandleIpcCommandAsync(IpcMessageEnvelope req, CancellationToken ct)
    {
        string responseJson = req.Command switch
        {
            "Ping" => JsonSerializer.Serialize(new { Status = "OK", Profile = optimizer.ActiveProfile.Name, SafeMode = optimizer.IsSafeModeEnabled }),
            "GetSnapshot" => JsonSerializer.Serialize(monitor.LatestSnapshot),
            "EmergencyStop" => await ExecuteEmergencyStopAsync(ct),
            _ => JsonSerializer.Serialize(new { Error = $"Perintah '{req.Command}' tidak dikenal." })
        };

        return new IpcMessageEnvelope(req.Id, 1, $"{req.Command}.Response", DateTimeOffset.UtcNow, responseJson);
    }

    private async Task<string> ExecuteEmergencyStopAsync(CancellationToken ct)
    {
        await optimizer.EmergencyStopAsync(ct);
        return JsonSerializer.Serialize(new { Status = "Stopped" });
    }
}
