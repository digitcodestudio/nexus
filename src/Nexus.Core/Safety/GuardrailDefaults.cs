namespace Nexus.Core.Safety;

public static class GuardrailDefaults
{
    public const double DefaultRamThresholdPercent = 75.0;
    public const double DefaultCpuThresholdPercent = 65.0;
    public const int DefaultSustainedCpuSeconds = 10;
    public const int DefaultIdleTimeoutMinutes = 30;
    public const int DefaultSamplingIntervalMs = 1000;
    public const int DefaultHistoryRetentionDays = 7;
    public const string IpcPipeName = "NEXUS.SystemOptimizer.IPC.v1";
}

