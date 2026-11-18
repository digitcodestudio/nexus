using System.Globalization;

namespace Nexus.Core.Formatting;

public static class ByteFormatter
{
    public static double ToGigabytes(long bytes) => bytes / (1024.0 * 1024.0 * 1024.0);
    public static double ToMegabytes(long bytes) => bytes / (1024.0 * 1024.0);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes >= 1024L * 1024 * 1024 * 1024)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB");
        if (bytes >= 1024L * 1024 * 1024)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):F2} GB");
        if (bytes >= 1024L * 1024)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):F1} MB");
        if (bytes >= 1024L)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F1} KB");
        return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
    }

    public static string FormatBytesPerSec(long bytesPerSec) => $"{FormatBytes(bytesPerSec)}/s";

    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return string.Create(CultureInfo.InvariantCulture, $"{(int)uptime.TotalDays}d {uptime.Hours:D2}h {uptime.Minutes:D2}m");
        return string.Create(CultureInfo.InvariantCulture, $"{uptime.Hours:D2}h {uptime.Minutes:D2}m {uptime.Seconds:D2}s");
    }
}
