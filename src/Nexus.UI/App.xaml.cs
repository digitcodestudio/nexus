using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Nexus.Core.Contracts;
using Nexus.Core.Safety;
using Nexus.CpuOptimizer;
using Nexus.MemoryOptimizer;
using Nexus.Monitoring;
using Nexus.Monitoring.Providers;
using Nexus.Optimizer;
using Nexus.ProcessManager;
using Nexus.ServiceManager;
using Nexus.Storage;
using Nexus.UI.Services;
using Nexus.UI.ViewModels;

namespace Nexus.UI;

public partial class App : Application
{
    private static IServiceProvider? _services;
    private static Mutex? _singleInstanceMutex;
    private MainWindow? _mainWindow;
    private CancellationTokenSource? _automationLoopCts;

    public static string ActiveBackdropDescription { get; set; } = "Mendeteksi...";

    public App()
    {
        UnhandledException += OnUnhandledException;
        InitializeComponent();
        _services = ConfigureServices();
    }

    public static T GetRequiredService<T>() where T : notnull
        => (_services ?? throw new InvalidOperationException("DI container belum siap.")).GetRequiredService<T>();

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Core & Keamanan
        services.AddSingleton<ISafetyPolicyValidator, SafetyPolicyValidator>();

        // Storage (SQLite WAL)
        services.AddSingleton<SqliteNexusRepository>();
        services.AddSingleton<INexusRepository>(sp => sp.GetRequiredService<SqliteNexusRepository>());

        // Monitoring, Process, Service & Optimization Engines (100% Native Windows API)
        services.AddSingleton<IHardwareInfoProvider, HardwareInfoProvider>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        services.AddSingleton<IWindowsServiceManager, WindowsServiceManager>();
        services.AddSingleton<IProcessManagerService, ProcessManagerService>();
        services.AddSingleton<TrayHygieneService>();
        services.AddSingleton<IMemoryOptimizer, MemoryOptimizerService>();
        services.AddSingleton<ICpuOptimizer, CpuOptimizerService>();
        services.AddSingleton<OptimizationEngine>();
        services.AddSingleton<IOptimizationEngine>(sp => sp.GetRequiredService<OptimizationEngine>());

        // UI Services & All 10 ViewModels
        services.AddSingleton<ThemeService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<MemoryOptimizerViewModel>();
        services.AddSingleton<CpuOptimizerViewModel>();
        services.AddSingleton<ProcessManagerViewModel>();
        services.AddSingleton<ServiceManagerViewModel>();
        services.AddSingleton<SmartAutomationViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<ActivityHistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Pastikan hanya 1 instansi NEXUS yang berjalan agar tidak menumpuk ikon di System Tray
            _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\NEXUS_System_Optimizer_SingleInstance_v1", out bool isFirstInstance);
            if (!isFirstInstance)
            {
                Exit();
                return;
            }

            bool startMinimized = Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            _mainWindow = GetRequiredService<MainWindow>();
            _mainWindow.Closed += OnMainWindowClosed;

            if (startMinimized)
            {
                _mainWindow.HideToTray();
            }
            else
            {
                _mainWindow.Activate();
            }

            _ = InitializeServicesAsync();
        }
        catch (Exception ex)
        {
            WriteDiagnosticLog($"OnLaunched gagal: {ex}");
            throw;
        }
    }

    private async Task InitializeServicesAsync()
    {
        try
        {
            var repo = GetRequiredService<INexusRepository>();
            await repo.InitializeAsync();

            var optimizer = GetRequiredService<OptimizationEngine>();
            await optimizer.InitializeAsync();

            var themeService = GetRequiredService<ThemeService>();
            await themeService.LoadSavedThemeAsync();

            var monitor = GetRequiredService<ISystemMonitorService>();
            await HydrateMonitorSettingsAsync(repo, monitor);
            await monitor.StartAsync();

            var settingsVm = GetRequiredService<SettingsViewModel>();
            var dbPath = GetRequiredService<SqliteNexusRepository>().DatabasePath;
            await settingsVm.LoadAsync(ActiveBackdropDescription, dbPath);

            var mainVm = GetRequiredService<MainViewModel>();
            mainVm.SyncOptimizerState();

            StartBackgroundEvaluationLoop(optimizer);
        }
        catch (Exception ex)
        {
            WriteDiagnosticLog($"InitializeServicesAsync gagal: {ex}");
        }
    }

    private void StartBackgroundEvaluationLoop(OptimizationEngine optimizer)
    {
        _automationLoopCts = new CancellationTokenSource();
        var ct = _automationLoopCts.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await optimizer.EvaluateRecommendationsAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    WriteDiagnosticLog($"Siklus otomatisasi latar belakang: {ex.Message}");
                }
            }
        }, ct);
    }

    private static async Task HydrateMonitorSettingsAsync(INexusRepository repo, ISystemMonitorService monitor)
    {
        if (double.TryParse(await repo.GetSettingAsync("guardrail.ram"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ram))
        {
            monitor.RamGuardrailPercent = Math.Clamp(ram, 40, 95);
        }

        if (double.TryParse(await repo.GetSettingAsync("guardrail.cpu"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu))
        {
            monitor.CpuGuardrailPercent = Math.Clamp(cpu, 30, 95);
        }

        if (int.TryParse(await repo.GetSettingAsync("monitor.sampling_ms"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
        {
            monitor.SamplingInterval = TimeSpan.FromMilliseconds(Math.Clamp(ms, 500, 10000));
        }
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_automationLoopCts is not null)
        {
            await _automationLoopCts.CancelAsync();
            _automationLoopCts.Dispose();
        }

        if (GetRequiredService<ICpuOptimizer>() is CpuOptimizerService cpuSvc)
        {
            await cpuSvc.RevertAllEcoQoSAsync();
        }

        var monitor = GetRequiredService<ISystemMonitorService>();
        await monitor.DisposeAsync();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteDiagnosticLog($"UnhandledException (tertangani aman): {e.Message}\n{e.Exception}");
        // Cegah aplikasi langsung tertutup akibat galat UI non-fatal
        e.Handled = true;
    }

    private static void WriteDiagnosticLog(string message)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"), $"[{DateTimeOffset.Now:O}] {message}\n\n");
        }
        catch
        {
        }
    }
}
