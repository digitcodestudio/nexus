using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Nexus.Core.Contracts;
using Nexus.Models;
using Nexus.ProcessManager.Native;

namespace Nexus.ProcessManager;

public sealed record TrayCleanupReport(
    int GhostToolbarsSwept,
    int DuplicateProcessesClosed,
    IReadOnlyList<string> ClosedTargets,
    string Summary);

/// <summary>
/// Safely purges dead/ghost icons in Windows System Tray (Notification Area &amp; Overflow)
/// and deduplicates redundant idle single-instance tray apps (e.g. duplicate Telegram processes).
/// </summary>
public sealed partial class TrayHygieneService
{
    private const uint WM_MOUSEMOVE = 0x0200;

    // Single-instance desktop tray apps that should not leave multiple orphaned root copies running.
    // Intentionally excludes multi-process browsers/Electron helpers & protected dev/system processes.
    private static readonly HashSet<string> SingleInstanceTrayApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "Telegram",
        "Telegram Desktop",
        "AyuGram",
        "Kotatogram",
        "WhatsApp",
        "Line",
        "WeChat",
        "Viber"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint hwndParent, nint hwndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    private readonly INexusRepository _repository;
    private readonly ILogger<TrayHygieneService> _logger;

    public TrayHygieneService(INexusRepository repository, ILogger<TrayHygieneService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Sweeps both the main Taskbar Notification Area and the Overflow flyout using standard WM_MOUSEMOVE
    /// messages so Windows Explorer immediately removes dead/phantom tray icons without restarting Explorer.
    /// </summary>
    public int PurgeGhostTrayIcons()
    {
        int swept = 0;
        try
        {
            // 1. Main Taskbar Notification Area (Windows 10 & Windows 11)
            nint shellTray = FindWindowW("Shell_TrayWnd", null);
            if (shellTray != nint.Zero)
            {
                nint trayNotify = FindWindowExW(shellTray, nint.Zero, "TrayNotifyWnd", null);
                if (trayNotify != nint.Zero)
                {
                    nint sysPager = FindWindowExW(trayNotify, nint.Zero, "SysPager", null);
                    if (sysPager != nint.Zero)
                    {
                        nint toolbar = FindWindowExW(sysPager, nint.Zero, "ToolbarWindow32", null);
                        if (toolbar != nint.Zero && SweepToolbar(toolbar)) swept++;
                    }

                    nint directToolbar = FindWindowExW(trayNotify, nint.Zero, "ToolbarWindow32", null);
                    if (directToolbar != nint.Zero && SweepToolbar(directToolbar)) swept++;
                }
            }

            // 2. Notification Overflow Popup ("^" flyout where ghost Telegram icons pile up)
            nint overflow = FindWindowW("NotifyIconOverflowWindow", null);
            if (overflow != nint.Zero)
            {
                nint overflowToolbar = FindWindowExW(overflow, nint.Zero, "ToolbarWindow32", null);
                if (overflowToolbar != nint.Zero && SweepToolbar(overflowToolbar)) swept++;
            }

            // 3. Windows 11 XAML Overflow Window fallback
            nint topOverflow = FindWindowW("TopLevelWindowForOverflowXamlIsland", null);
            if (topOverflow != nint.Zero && SweepToolbar(topOverflow)) swept++;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal menyapu ikon tray hantu.");
        }

        return swept;
    }

    public async Task<TrayCleanupReport> CleanupDuplicatesAndGhostIconsAsync(bool closeDuplicateInstances = true, CancellationToken ct = default)
    {
        var closedTargets = new List<string>();
        int closedCount = 0;

        if (closeDuplicateInstances)
        {
            await Task.Run(() =>
            {
                int fgPid = 0;
                nint fgHwnd = ProcessInterop.GetForegroundWindow();
                if (fgHwnd != nint.Zero)
                {
                    ProcessInterop.GetWindowThreadProcessId(fgHwnd, out fgPid);
                }

                foreach (string appName in SingleInstanceTrayApps)
                {
                    ct.ThrowIfCancellationRequested();
                    Process[] instances;
                    try { instances = Process.GetProcessesByName(appName); }
                    catch { continue; }

                    try
                    {
                        if (instances.Length <= 1) continue;

                        // Keep the foreground instance if present, otherwise keep the newest instance
                        var ordered = instances
                            .Select(p =>
                            {
                                DateTime start = DateTime.MinValue;
                                try { start = p.StartTime; } catch { }
                                return (Proc: p, Start: start, IsFg: p.Id == fgPid);
                            })
                            .OrderByDescending(x => x.IsFg)
                            .ThenByDescending(x => x.Start)
                            .ToList();

                        // Skip index 0 (kept active instance); gracefully close redundant duplicates
                        for (int i = 1; i < ordered.Count; i++)
                        {
                            var dup = ordered[i].Proc;
                            try
                            {
                                bool sentClose = false;
                                if (dup.MainWindowHandle != nint.Zero)
                                {
                                    sentClose = ProcessInterop.PostMessageW(dup.MainWindowHandle, ProcessInterop.WM_CLOSE, nint.Zero, nint.Zero);
                                }

                                // If headless/stuck duplicate tray copy has no main window, request CloseMainWindow or gentle exit
                                if (!sentClose)
                                {
                                    sentClose = dup.CloseMainWindow();
                                }

                                if (!sentClose)
                                {
                                    // For clearly redundant duplicate Telegram/chat tray copies with no window handle
                                    dup.Kill(entireProcessTree: false);
                                    sentClose = true;
                                }

                                if (sentClose)
                                {
                                    closedCount++;
                                    closedTargets.Add($"{dup.ProcessName} (PID {dup.Id})");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Tidak dapat menutup duplikat {Name} PID {Pid}", dup.ProcessName, dup.Id);
                            }
                        }
                    }
                    finally
                    {
                        foreach (var p in instances) p.Dispose();
                    }
                }
            }, ct);

            if (closedCount > 0)
            {
                await Task.Delay(350, ct);
            }
        }

        int swept = PurgeGhostTrayIcons();

        string summary = closedCount > 0
            ? $"Pembersihan tray selesai: {closedCount} instansi duplikat ({string.Join(", ", closedTargets.Take(3))}) ditutup & {swept} area tray dibersihkan dari ikon hantu."
            : $"Area System Tray dibersihkan ({swept} toolbar disapu). Tidak ditemukan proses tray duplikat aktif.";

        if (closedCount > 0)
        {
            await _repository.LogActivityAsync(new ActivityLogEntry(
                0,
                DateTimeOffset.UtcNow,
                ActivitySeverity.Success,
                "TrayHygiene",
                "CleanupDuplicates",
                string.Join(", ", closedTargets.Take(3)),
                summary,
                IsReversible: false,
                RollbackReferenceId: null), ct);
        }

        return new TrayCleanupReport(swept, closedCount, closedTargets, summary);
    }

    private static bool SweepToolbar(nint hwnd)
    {
        if (!GetClientRect(hwnd, out var rect)) return false;
        int width = Math.Clamp(rect.Right - rect.Left, 0, 1200);
        int height = Math.Clamp(rect.Bottom - rect.Top, 0, 800);
        if (width <= 0 || height <= 0) return false;

        for (int y = 4; y < height; y += 16)
        {
            for (int x = 4; x < width; x += 16)
            {
                nint lParam = (nint)((y << 16) | (x & 0xFFFF));
                ProcessInterop.PostMessageW(hwnd, WM_MOUSEMOVE, nint.Zero, lParam);
            }
        }

        return true;
    }
}

