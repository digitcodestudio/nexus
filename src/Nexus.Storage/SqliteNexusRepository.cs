using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Nexus.Core.Contracts;
using Nexus.Core.Safety;
using Nexus.Models;

namespace Nexus.Storage;

public sealed class SqliteNexusRepository : INexusRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteNexusRepository> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public string DatabasePath { get; }

    public SqliteNexusRepository(ILogger<SqliteNexusRepository> logger, string? databasePath = null)
    {
        _logger = logger;
        DatabasePath = databasePath ?? GetDefaultDatabasePath();

        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public static string GetDefaultDatabasePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "NEXUS", "nexus.db");
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await using var conn = await OpenConnectionAsync(ct);

            await using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;";
                await pragma.ExecuteNonQueryAsync(ct);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS app_settings (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL,
                        updated_utc TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS optimization_profiles (
                        id TEXT PRIMARY KEY,
                        profile_type TEXT NOT NULL UNIQUE,
                        name TEXT NOT NULL,
                        description TEXT NOT NULL,
                        ram_threshold_percent REAL NOT NULL,
                        cpu_threshold_percent REAL NOT NULL,
                        sustained_cpu_seconds INTEGER NOT NULL,
                        idle_timeout_minutes INTEGER NOT NULL,
                        enable_ecoqos INTEGER NOT NULL,
                        enable_service_recommendations INTEGER NOT NULL,
                        safe_mode_only INTEGER NOT NULL,
                        target_power_mode TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS app_whitelist (
                        process_name TEXT PRIMARY KEY,
                        is_immutable INTEGER NOT NULL DEFAULT 0,
                        added_utc TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS service_whitelist (
                        service_name TEXT PRIMARY KEY,
                        is_immutable INTEGER NOT NULL DEFAULT 0,
                        opt_in_auto_optimize INTEGER NOT NULL DEFAULT 0,
                        updated_utc TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS automation_rules (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL,
                        is_enabled INTEGER NOT NULL,
                        trigger_type TEXT NOT NULL,
                        threshold_percent REAL,
                        idle_minutes INTEGER,
                        safety_checks_json TEXT NOT NULL,
                        action_type TEXT NOT NULL,
                        cooldown_minutes INTEGER NOT NULL,
                        target_filter TEXT
                    );

                    CREATE TABLE IF NOT EXISTS service_config_backups (
                        id TEXT PRIMARY KEY,
                        service_name TEXT NOT NULL,
                        original_start_type TEXT NOT NULL,
                        original_delayed_auto INTEGER NOT NULL,
                        original_state TEXT NOT NULL,
                        captured_utc TEXT NOT NULL,
                        reason TEXT NOT NULL,
                        rolled_back_utc TEXT
                    );

                    CREATE TABLE IF NOT EXISTS activity_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        timestamp_utc TEXT NOT NULL,
                        severity TEXT NOT NULL,
                        module TEXT NOT NULL,
                        action TEXT NOT NULL,
                        target TEXT NOT NULL,
                        summary TEXT NOT NULL,
                        is_reversible INTEGER NOT NULL,
                        rollback_ref_id TEXT
                    );

                    CREATE TABLE IF NOT EXISTS monitoring_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        timestamp_utc TEXT NOT NULL,
                        cpu_percent REAL NOT NULL,
                        ram_percent REAL NOT NULL,
                        ram_used_bytes INTEGER NOT NULL,
                        disk_active_percent REAL NOT NULL,
                        net_down_bps INTEGER NOT NULL,
                        net_up_bps INTEGER NOT NULL
                    );

                    CREATE INDEX IF NOT EXISTS idx_monitoring_history_ts ON monitoring_history(timestamp_utc);
                    CREATE INDEX IF NOT EXISTS idx_service_backups_name ON service_config_backups(service_name, captured_utc DESC);
                    CREATE INDEX IF NOT EXISTS idx_activity_history_ts ON activity_history(timestamp_utc DESC);
                    """;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await SeedDefaultsAsync(conn, ct);
            _initialized = true;
            _logger.LogInformation("NEXUS SQLite database initialized at {DatabasePath}", DatabasePath);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task SeedDefaultsAsync(SqliteConnection conn, CancellationToken ct)
    {
        var defaultProfiles = new[]
        {
            new OptimizationProfile(
                Guid.Parse("11111111-1111-1111-1111-111111111101"),
                ProfileType.Balanced,
                "Balanced",
                "Normal Windows behavior with moderate background optimization. Guardrails: RAM <= 75%, CPU <= 65%.",
                GuardrailDefaults.DefaultRamThresholdPercent,
                GuardrailDefaults.DefaultCpuThresholdPercent,
                GuardrailDefaults.DefaultSustainedCpuSeconds,
                GuardrailDefaults.DefaultIdleTimeoutMinutes,
                EnableEcoQoS: true,
                EnableServiceRecommendations: true,
                SafeModeOnly: true,
                WindowsPowerMode.Balanced),
            new OptimizationProfile(
                Guid.Parse("11111111-1111-1111-1111-111111111102"),
                ProfileType.Development,
                "Development",
                "Prioritizes Antigravity, VS Code, browsers, databases, terminals, and build tools without interrupting active workloads.",
                GuardrailDefaults.DefaultRamThresholdPercent,
                GuardrailDefaults.DefaultCpuThresholdPercent,
                SustainedCpuSeconds: 15,
                IdleTimeoutMinutes: 30,
                EnableEcoQoS: true,
                EnableServiceRecommendations: true,
                SafeModeOnly: true,
                WindowsPowerMode.BestPerformance),
            new OptimizationProfile(
                Guid.Parse("11111111-1111-1111-1111-111111111103"),
                ProfileType.HeavyWorkload,
                "Heavy Workload",
                "Maximizes CPU performance and memory headroom while suppressing intrusive background automation.",
                RamThresholdPercent: 80.0,
                CpuThresholdPercent: 75.0,
                SustainedCpuSeconds: 20,
                IdleTimeoutMinutes: 45,
                EnableEcoQoS: false,
                EnableServiceRecommendations: true,
                SafeModeOnly: true,
                WindowsPowerMode.BestPerformance),
            new OptimizationProfile(
                Guid.Parse("11111111-1111-1111-1111-111111111104"),
                ProfileType.PowerSaving,
                "Power Saving",
                "Uses Windows Efficiency Mode (EcoQoS) and Best Power Efficiency overlay to curb background activity.",
                RamThresholdPercent: 70.0,
                CpuThresholdPercent: 55.0,
                SustainedCpuSeconds: 8,
                IdleTimeoutMinutes: 20,
                EnableEcoQoS: true,
                EnableServiceRecommendations: true,
                SafeModeOnly: true,
                WindowsPowerMode.BestPowerEfficiency)
        };

        foreach (var profile in defaultProfiles)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO optimization_profiles (
                    id, profile_type, name, description, ram_threshold_percent, cpu_threshold_percent,
                    sustained_cpu_seconds, idle_timeout_minutes, enable_ecoqos, enable_service_recommendations,
                    safe_mode_only, target_power_mode)
                VALUES ($id, $type, $name, $desc, $ram, $cpu, $sustained, $idle, $eco, $svc, $safe, $power);
                """;
            cmd.Parameters.AddWithValue("$id", profile.Id.ToString());
            cmd.Parameters.AddWithValue("$type", profile.Type.ToString());
            cmd.Parameters.AddWithValue("$name", profile.Name);
            cmd.Parameters.AddWithValue("$desc", profile.Description);
            cmd.Parameters.AddWithValue("$ram", profile.RamThresholdPercent);
            cmd.Parameters.AddWithValue("$cpu", profile.CpuThresholdPercent);
            cmd.Parameters.AddWithValue("$sustained", profile.SustainedCpuSeconds);
            cmd.Parameters.AddWithValue("$idle", profile.IdleTimeoutMinutes);
            cmd.Parameters.AddWithValue("$eco", profile.EnableEcoQoS ? 1 : 0);
            cmd.Parameters.AddWithValue("$svc", profile.EnableServiceRecommendations ? 1 : 0);
            cmd.Parameters.AddWithValue("$safe", profile.SafeModeOnly ? 1 : 0);
            cmd.Parameters.AddWithValue("$power", profile.TargetPowerMode.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        foreach (var app in ProtectedLists.DefaultProtectedDeveloperApps)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO app_whitelist (process_name, is_immutable, added_utc)
                VALUES ($name, 0, $now);
                """;
            cmd.Parameters.AddWithValue("$name", app);
            cmd.Parameters.AddWithValue("$now", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result?.ToString();
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_settings (key, value, updated_utc)
            VALUES ($key, $value, $now)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<OptimizationProfile>> GetProfilesAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var list = new List<OptimizationProfile>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, profile_type, name, description, ram_threshold_percent, cpu_threshold_percent,
                   sustained_cpu_seconds, idle_timeout_minutes, enable_ecoqos, enable_service_recommendations,
                   safe_mode_only, target_power_mode
            FROM optimization_profiles
            ORDER BY id;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OptimizationProfile(
                Guid.Parse(reader.GetString(0)),
                Enum.Parse<ProfileType>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8) == 1,
                reader.GetInt32(9) == 1,
                reader.GetInt32(10) == 1,
                Enum.Parse<WindowsPowerMode>(reader.GetString(11))));
        }

        return list;
    }

    public async Task SaveProfileAsync(OptimizationProfile profile, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO optimization_profiles (
                id, profile_type, name, description, ram_threshold_percent, cpu_threshold_percent,
                sustained_cpu_seconds, idle_timeout_minutes, enable_ecoqos, enable_service_recommendations,
                safe_mode_only, target_power_mode)
            VALUES ($id, $type, $name, $desc, $ram, $cpu, $sustained, $idle, $eco, $svc, $safe, $power)
            ON CONFLICT(profile_type) DO UPDATE SET
                name = excluded.name,
                description = excluded.description,
                ram_threshold_percent = excluded.ram_threshold_percent,
                cpu_threshold_percent = excluded.cpu_threshold_percent,
                sustained_cpu_seconds = excluded.sustained_cpu_seconds,
                idle_timeout_minutes = excluded.idle_timeout_minutes,
                enable_ecoqos = excluded.enable_ecoqos,
                enable_service_recommendations = excluded.enable_service_recommendations,
                safe_mode_only = excluded.safe_mode_only,
                target_power_mode = excluded.target_power_mode;
            """;
        cmd.Parameters.AddWithValue("$id", profile.Id.ToString());
        cmd.Parameters.AddWithValue("$type", profile.Type.ToString());
        cmd.Parameters.AddWithValue("$name", profile.Name);
        cmd.Parameters.AddWithValue("$desc", profile.Description);
        cmd.Parameters.AddWithValue("$ram", profile.RamThresholdPercent);
        cmd.Parameters.AddWithValue("$cpu", profile.CpuThresholdPercent);
        cmd.Parameters.AddWithValue("$sustained", profile.SustainedCpuSeconds);
        cmd.Parameters.AddWithValue("$idle", profile.IdleTimeoutMinutes);
        cmd.Parameters.AddWithValue("$eco", profile.EnableEcoQoS ? 1 : 0);
        cmd.Parameters.AddWithValue("$svc", profile.EnableServiceRecommendations ? 1 : 0);
        cmd.Parameters.AddWithValue("$safe", profile.SafeModeOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("$power", profile.TargetPowerMode.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetProtectedAppsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var apps = new List<string>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT process_name FROM app_whitelist ORDER BY process_name;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            apps.Add(reader.GetString(0));
        }
        return apps;
    }

    public async Task SetProtectedAppAsync(string processName, bool isProtected, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var normalized = ProtectedLists.NormalizeProcessName(processName);
        if (ProtectedLists.IsImmutableSystemProcess(normalized)) return;

        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (isProtected)
        {
            cmd.CommandText = """
                INSERT OR IGNORE INTO app_whitelist (process_name, is_immutable, added_utc)
                VALUES ($name, 0, $now);
                """;
            cmd.Parameters.AddWithValue("$name", normalized);
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        }
        else
        {
            cmd.CommandText = "DELETE FROM app_whitelist WHERE process_name = $name AND is_immutable = 0;";
            cmd.Parameters.AddWithValue("$name", normalized);
        }

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveServiceBackupAsync(ServiceConfigurationSnapshot snapshot, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO service_config_backups (
                id, service_name, original_start_type, original_delayed_auto, original_state,
                captured_utc, reason, rolled_back_utc)
            VALUES ($id, $name, $startType, $delayed, $state, $captured, $reason, $rolledBack);
            """;
        cmd.Parameters.AddWithValue("$id", snapshot.Id.ToString());
        cmd.Parameters.AddWithValue("$name", snapshot.ServiceName);
        cmd.Parameters.AddWithValue("$startType", snapshot.OriginalStartupType.ToString());
        cmd.Parameters.AddWithValue("$delayed", snapshot.OriginalDelayedAutoStart ? 1 : 0);
        cmd.Parameters.AddWithValue("$state", snapshot.OriginalState.ToString());
        cmd.Parameters.AddWithValue("$captured", snapshot.CapturedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$reason", snapshot.Reason);
        cmd.Parameters.AddWithValue("$rolledBack", (object?)snapshot.RolledBackUtc?.ToString("O") ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ServiceConfigurationSnapshot?> GetLatestServiceBackupAsync(string serviceName, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, service_name, original_start_type, original_delayed_auto, original_state,
                   captured_utc, reason, rolled_back_utc
            FROM service_config_backups
            WHERE service_name = $name AND rolled_back_utc IS NULL
            ORDER BY captured_utc DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$name", serviceName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return ReadServiceSnapshot(reader);
    }

    public async Task<IReadOnlyList<ServiceConfigurationSnapshot>> GetAllServiceBackupsAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var list = new List<ServiceConfigurationSnapshot>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, service_name, original_start_type, original_delayed_auto, original_state,
                   captured_utc, reason, rolled_back_utc
            FROM service_config_backups
            ORDER BY captured_utc DESC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadServiceSnapshot(reader));
        }

        return list;
    }

    public async Task MarkServiceBackupRolledBackAsync(Guid backupId, DateTimeOffset rolledBackUtc, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE service_config_backups SET rolled_back_utc = $ts WHERE id = $id;";
        cmd.Parameters.AddWithValue("$ts", rolledBackUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$id", backupId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task LogActivityAsync(ActivityLogEntry entry, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO activity_history (
                timestamp_utc, severity, module, action, target, summary, is_reversible, rollback_ref_id)
            VALUES ($ts, $sev, $mod, $act, $target, $sum, $rev, $ref);
            """;
        cmd.Parameters.AddWithValue("$ts", entry.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$sev", entry.Severity.ToString());
        cmd.Parameters.AddWithValue("$mod", entry.Module);
        cmd.Parameters.AddWithValue("$act", entry.Action);
        cmd.Parameters.AddWithValue("$target", entry.Target);
        cmd.Parameters.AddWithValue("$sum", entry.Summary);
        cmd.Parameters.AddWithValue("$rev", entry.IsReversible ? 1 : 0);
        cmd.Parameters.AddWithValue("$ref", (object?)entry.RollbackReferenceId?.ToString() ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ActivityLogEntry>> GetActivityHistoryAsync(int limit = 200, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var list = new List<ActivityLogEntry>();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, timestamp_utc, severity, module, action, target, summary, is_reversible, rollback_ref_id
            FROM activity_history
            ORDER BY id DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ActivityLogEntry(
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                Enum.Parse<ActivitySeverity>(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7) == 1,
                reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8))));
        }

        return list;
    }

    public async Task AppendMonitoringSampleAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO monitoring_history (
                timestamp_utc, cpu_percent, ram_percent, ram_used_bytes,
                disk_active_percent, net_down_bps, net_up_bps)
            VALUES ($ts, $cpu, $ram, $ramBytes, $disk, $down, $up);
            """;
        cmd.Parameters.AddWithValue("$ts", snapshot.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$cpu", snapshot.Cpu.TotalUsagePercent);
        cmd.Parameters.AddWithValue("$ram", snapshot.Memory.UsagePercent);
        cmd.Parameters.AddWithValue("$ramBytes", snapshot.Memory.UsedBytes);
        cmd.Parameters.AddWithValue("$disk", snapshot.Disk.ActiveTimePercent);
        cmd.Parameters.AddWithValue("$down", snapshot.Network.DownloadBytesPerSec);
        cmd.Parameters.AddWithValue("$up", snapshot.Network.UploadBytesPerSec);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task PruneMonitoringHistoryAsync(int retentionDays, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, retentionDays)).ToString("O");
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM monitoring_history WHERE timestamp_utc < $cutoff;";
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static ServiceConfigurationSnapshot ReadServiceSnapshot(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            Enum.Parse<ServiceStartupType>(reader.GetString(2)),
            reader.GetInt32(3) == 1,
            Enum.Parse<ServiceState>(reader.GetString(4)),
            DateTimeOffset.Parse(reader.GetString(5)),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)));
}

