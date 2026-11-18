namespace Nexus.Core.Safety;

/// <summary>
/// Immutable system protection lists that cannot be bypassed by UI, settings, or automation rules.
/// </summary>
public static class ProtectedLists
{
    public static readonly IReadOnlySet<string> ImmutableCriticalServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Windows Security & Defender
        "WinDefend", "WdNisSvc", "WdFilter", "WdBoot", "SecurityHealthService",
        "Sense", "mpssvc", "BFE", "wscsvc",

        // Core OS, RPC, Authentication & Eventing
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "VaultSvc",
        "BrokerInfrastructure", "Power", "ProfSvc", "SystemEventsBroker",
        "EventLog", "CryptSvc", "KeyIso", "Winmgmt", "Schedule",
        "UserManager", "StateRepository", "CoreMessagingRegistrar", "gpsvc",

        // Plug and Play, Hardware & Storage
        "PlugPlay", "DeviceInstall", "DsmSvc", "vds", "StorSvc",
        "ShellHWDetection", "swprv", "VSS", "defragsvc",

        // Core Networking
        "Dhcp", "Dnscache", "nsi", "Netman", "NlaSvc", "netprofm",
        "LanmanWorkstation", "LanmanServer", "WlanSvc", "Tcpip",
        "WinHttpAutoProxySvc", "iphlpsvc",

        // Audio & Windows Update
        "Audiosrv", "AudioEndpointBuilder", "wuauserv", "TrustedInstaller",
        "BITS", "UsoSvc", "WaaSMedicSvc"
    };

    public static readonly IReadOnlySet<string> ImmutableSystemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "dwm", "explorer", "sihost", "fontdrvhost",
        "MsMpEng", "NisSrv", "SecurityHealthService", "audiodg", "conhost",
        "Nexus.UI", "Nexus.BackgroundService"
    };

    public static readonly IReadOnlySet<string> DefaultProtectedDeveloperApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Antigravity", "Code", "devenv", "rider64", "idea64", "datagrip64", "pycharm64",
        "WindowsTerminal", "wt", "pwsh", "powershell", "cmd", "wsl",
        "sqlservr", "postgres", "mysqld", "mongod", "redis-server", "Ssms", "dbeaver",
        "Docker Desktop", "com.docker.backend", "vmmem", "vmmemWSL"
    };

    public static readonly IReadOnlySet<string> ActiveBuildAndWorkProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "msbuild", "dotnet", "csc", "vbc", "cl", "link", "lld",
        "node", "npm", "pnpm", "yarn", "bun", "vite", "webpack", "esbuild",
        "cargo", "rustc", "go", "javac", "gradle", "mvn", "python", "python3"
    };

    public static bool IsImmutableCriticalService(string serviceName)
        => !string.IsNullOrWhiteSpace(serviceName) && ImmutableCriticalServices.Contains(serviceName.Trim());

    public static bool IsImmutableSystemProcess(string processName)
        => !string.IsNullOrWhiteSpace(processName) && ImmutableSystemProcesses.Contains(NormalizeProcessName(processName));

    public static string NormalizeProcessName(string processName)
    {
        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}

