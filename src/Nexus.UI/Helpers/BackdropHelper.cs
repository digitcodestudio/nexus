using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Nexus.UI.Helpers;

public static class BackdropHelper
{
    public static string ApplyOptimalBackdrop(Window window)
    {
        try
        {
            // Windows 11 Build >= 22000 supports Mica
            if (Environment.OSVersion.Version.Build >= 22000 && MicaController.IsSupported())
            {
                window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                return "Mica (Windows 11)";
            }

            // Windows 10 (e.g. Build 19045) graceful fallback to Acrylic or clean solid charcoal
            if (DesktopAcrylicController.IsSupported())
            {
                window.SystemBackdrop = new DesktopAcrylicBackdrop();
                return "Desktop Acrylic (Windows 10 Fallback)";
            }
        }
        catch
        {
            // Fall back silently to solid Fluent surface
        }

        return "Solid Fluent Surface";
    }
}

