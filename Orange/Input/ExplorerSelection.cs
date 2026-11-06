using System.Runtime.InteropServices;
using Orange.Core;
using static Orange.Input.Native;

namespace Orange.Input;

/// <summary>
/// Reads which files are selected in the Explorer window (or the desktop) under a screen point.
/// Must run on an STA thread (the UI dispatcher), never inside a hook callback.
/// </summary>
internal static class ExplorerSelection
{
    public static IReadOnlyList<string> At(int x, int y)
    {
        try
        {
            var root = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), GA_ROOT);
            if (root == IntPtr.Zero) return [];
            var cls = ClassOf(root);
            return cls is "Progman" or "WorkerW" ? FromDesktop() : FromWindow(root);
        }
        catch (Exception ex)
        {
            JobRunner.Log($"Explorer selection failed: {ex.Message}");
            return [];
        }
    }

    private static IReadOnlyList<string> FromDesktop()
    {
        var windows = (IShellWindows)new ShellWindowsClass();
        try
        {
            object loc = CSIDL_DESKTOP, root = null!;
            int hr = windows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out var dispatch);
            return hr == 0 && dispatch is not null ? Selected(dispatch) : [];
        }
        finally { Marshal.FinalReleaseComObject(windows); }
    }

    private static IReadOnlyList<string> FromWindow(IntPtr root)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return [];
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            foreach (dynamic w in shell.Windows())
            {
                try
                {
                    if ((IntPtr)(long)w.HWND != root) continue;
                    // Windows 11 tabs share one HWND; only the visible tab is the one the user sees.
                    if (!IsActiveTab(w)) continue;
                    return Selected(w);
                }
                catch (COMException) { } // e.g. an Internet Explorer window or a closing tab
            }
            return [];
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static bool IsActiveTab(object browser)
    {
        var sid = SID_STopLevelBrowser;
        var iid = IID_IShellBrowser;
        if (((IOleServiceProvider)browser).QueryService(ref sid, ref iid, out var sb) != 0 || sb is null) return true;
        ((IShellBrowser)sb).QueryActiveShellView(out var view);
        ((IShellView)view).GetWindow(out var defView);
        return IsWindowVisible(defView);
    }

    private static IReadOnlyList<string> Selected(dynamic browser)
    {
        var list = new List<string>();
        foreach (dynamic item in browser.Document.SelectedItems())
        {
            string? path = item.Path;
            // Only real files/folders: virtual items (This PC, Recycle Bin…) have no usable path.
            if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path))) list.Add(path);
        }
        return list;
    }
}
