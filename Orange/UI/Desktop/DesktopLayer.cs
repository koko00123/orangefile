using Orange.Core;
using Orange.Desktop.Layout;
using static Orange.Input.Native;

namespace Orange.UI.Desktop;

/// <summary>Finds the windows box windows attach to: the wallpaper layer behind the icons, or Progman.</summary>
internal static class DesktopLayer
{
    /// <summary>Undocumented Progman message that creates the WorkerW used by animated wallpapers.</summary>
    private const uint WM_SPAWN_WORKER = 0x052C;

    public static IntPtr Progman => FindWindow("Progman", null);

    /// <summary>
    /// The WorkerW window drawn between the wallpaper and the icons, or zero if this Windows build has none.
    /// TODO(verify): P1 — Windows 11 24H2+ moved the WorkerW inside Progman; both layouts are tried.
    /// </summary>
    public static IntPtr FindWallpaperLayer()
    {
        var progman = Progman;
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        // 0xD/1 is what wallpaper apps send; older builds also accept 0/0.
        SendMessageTimeout(progman, WM_SPAWN_WORKER, new IntPtr(0xD), new IntPtr(1), SMTO_NORMAL, 1000, out _);

        // 24H2 and later: WorkerW is a child of Progman, behind SHELLDLL_DefView.
        var inside = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (inside != IntPtr.Zero) return inside;

        // Older builds: the top-level WorkerW right after the window that hosts SHELLDLL_DefView.
        IntPtr found = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            found = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Screen position of a window's client origin (child windows are placed relative to it).</summary>
    public static PixelPoint ClientOrigin(IntPtr hwnd)
    {
        var p = new POINT();
        if (!ClientToScreen(hwnd, ref p)) JobRunner.Log("Desktop: could not read the wallpaper layer origin");
        return new PixelPoint(p.X, p.Y);
    }
}
