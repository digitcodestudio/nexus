using Nexus.Core.Safety;
using Nexus.Models;

namespace Nexus.ServiceManager;

public static class ServiceClassifier
{
    private static readonly HashSet<string> SecurityServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "WdNisSvc", "WdFilter", "WdBoot", "SecurityHealthService",
        "Sense", "mpssvc", "BFE", "wscsvc", "MDCoreSvc", "webthreatdefsvc"
    };

    private static readonly HashSet<string> NetworkingServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dhcp", "Dnscache", "nsi", "Netman", "NlaSvc", "netprofm",
        "LanmanWorkstation", "LanmanServer", "WlanSvc", "Tcpip",
        "WinHttpAutoProxySvc", "iphlpsvc", "WwanSvc", "RasMan"
    };

    private static readonly HashSet<string> HardwareServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "PlugPlay", "DeviceInstall", "DsmSvc", "vds", "StorSvc",
        "ShellHWDetection", "Audiosrv", "AudioEndpointBuilder", "bthserv",
        "DisplayEnhancementService", "NVDisplay.ContainerLocalSystem"
    };

    private static readonly string[] DevelopmentKeywords =
    [
        "sql", "mssql", "postgres", "mysql", "mariadb", "mongo", "redis",
        "docker", "wsl", "w3svc", "iis", "apache", "nginx", "rabbitmq", "ollama"
    ];

    private static readonly string[] OptimizationCandidatePrefixes =
    [
        "gupdate", "gupdatem", "GoogleUpdater", "GoogleChromeElevationService",
        "edgeupdate", "edgeupdatem", "MicrosoftEdgeElevationService",
        "AdobeARMservice", "AdobeUpdateService", "BraveUpdate", "brave",
        "DiagTrack", "dmwappushservice", "MapsBroker", "RetailDemo",
        "Fax", "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc",
        "WSearch", "SysMain"
    ];

    public static (ServiceCategory Category, ServiceRecommendation Recommendation, bool IsCriticalProtected) Classify(
        string serviceName,
        string displayName,
        string? binaryPath,
        ServiceStartupType startupType,
        bool isTriggerStart)
    {
        if (SecurityServices.Contains(serviceName))
            return (ServiceCategory.Security, ServiceRecommendation.KeepProtected, true);

        if (ProtectedLists.IsImmutableCriticalService(serviceName))
        {
            if (NetworkingServices.Contains(serviceName))
                return (ServiceCategory.Networking, ServiceRecommendation.KeepProtected, true);
            if (HardwareServices.Contains(serviceName))
                return (ServiceCategory.Hardware, ServiceRecommendation.KeepProtected, true);
            return (ServiceCategory.CriticalWindows, ServiceRecommendation.KeepProtected, true);
        }

        if (NetworkingServices.Contains(serviceName))
            return (ServiceCategory.Networking, ServiceRecommendation.KeepDefault, false);

        if (HardwareServices.Contains(serviceName))
            return (ServiceCategory.Hardware, ServiceRecommendation.KeepDefault, false);

        string combined = $"{serviceName} {displayName}".ToLowerInvariant();
        if (DevelopmentKeywords.Any(k => combined.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            var rec = startupType == ServiceStartupType.Automatic
                ? ServiceRecommendation.ReviewManual
                : ServiceRecommendation.KeepDefault;
            return (ServiceCategory.Development, rec, false);
        }

        if (OptimizationCandidatePrefixes.Any(p => serviceName.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
            combined.Contains("updater", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("telemetry", StringComparison.OrdinalIgnoreCase))
        {
            return (ServiceCategory.OptimizationCandidate, ServiceRecommendation.OptInOptimizationCandidate, false);
        }

        bool isMicrosoftSystemBinary = binaryPath is not null &&
            (binaryPath.Contains(@"\Windows\System32", StringComparison.OrdinalIgnoreCase) ||
             binaryPath.Contains(@"\Windows\SysWOW64", StringComparison.OrdinalIgnoreCase));

        if (!isMicrosoftSystemBinary && !string.IsNullOrWhiteSpace(binaryPath))
        {
            var rec = startupType == ServiceStartupType.Automatic && !isTriggerStart
                ? ServiceRecommendation.ReviewManual
                : ServiceRecommendation.KeepDefault;
            return (ServiceCategory.UserInstalled, rec, false);
        }

        return (ServiceCategory.Optional, ServiceRecommendation.KeepDefault, false);
    }
}

