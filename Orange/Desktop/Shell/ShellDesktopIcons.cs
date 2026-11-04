using System.Runtime.InteropServices;
using Orange.Core;
using Orange.Desktop.Layout;
using Orange.Desktop.Model;
using static Orange.Input.Native;

namespace Orange.Desktop.Shell;

/// <summary>
/// Reads and moves desktop icons through Explorer's own folder view (IFolderView2).
/// Must be used from an STA thread (the UI thread). A view broken by an Explorer restart is
/// dropped and fetched again once per call.
/// </summary>
internal sealed class ShellDesktopIcons : IDesktopIcons
{
    private IFolderView2? _view;
    private IShellFolder? _folder;
    private IntPtr _listView;

    public IReadOnlyList<DesktopItem> GetItems() => WithView(() =>
    {
        var origin = ListOrigin();
        var items = new List<DesktopItem>();
        foreach (var (pidl, name) in Pidls())
        {
            try
            {
                _view!.GetItemPosition(pidl, out var pt);
                items.Add(new DesktopItem(name, KindOf(pidl), new PixelPoint(pt.X + origin.X, pt.Y + origin.Y)));
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        return items;
    });

    public PixelSize GetSpacing() => WithView(() =>
    {
        _view!.GetSpacing(out var pt);
        return new PixelSize(pt.X, pt.Y);
    });

    public int SetPositions(IReadOnlyList<(string Name, PixelPoint Position)> moves)
    {
        if (moves.Count == 0) return 0;
        return WithView(() =>
        {
            var origin = ListOrigin();
            var wanted = new Dictionary<string, PixelPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in moves) wanted[m.Name] = m.Position;

            var pidls = new List<IntPtr>();
            var points = new List<POINT>();
            try
            {
                foreach (var (pidl, name) in Pidls())
                {
                    if (wanted.TryGetValue(name, out var p))
                    {
                        pidls.Add(pidl);
                        points.Add(new POINT { X = p.X - origin.X, Y = p.Y - origin.Y });
                    }
                    else Marshal.FreeCoTaskMem(pidl);
                }
                // One call for the whole batch, so Explorer repaints once.
                if (pidls.Count > 0)
                    _view!.SelectAndPositionItems((uint)pidls.Count, [.. pidls], [.. points], SVSI_POSITIONITEM);
                return pidls.Count;
            }
            finally
            {
                foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
            }
        });
    }

    public bool EnsureFreePositioning() => WithView(() =>
    {
        // TODO(verify): P4 — whether Explorer keeps these flags after it restarts.
        const uint mask = FWF_AUTOARRANGE | FWF_SNAPTOGRID;
        _view!.GetCurrentFolderFlags(out var flags);
        if ((flags & mask) == 0) return false;
        _view.SetCurrentFolderFlags(mask, 0);
        return true;
    });

    public void RestoreArrangement(DesktopViewInfo before) => WithView(() =>
    {
        uint flags = (before.AutoArrange ? FWF_AUTOARRANGE : 0) | (before.SnapToGrid ? FWF_SNAPTOGRID : 0);
        _view!.SetCurrentFolderFlags(FWF_AUTOARRANGE | FWF_SNAPTOGRID, flags);
        return true;
    });

    public DesktopViewInfo GetViewInfo() => WithView(() =>
    {
        _view!.GetCurrentFolderFlags(out var flags);
        _view.GetSpacing(out var s);
        _view.GetDefaultSpacing(out var d);
        return new DesktopViewInfo((flags & FWF_AUTOARRANGE) != 0, (flags & FWF_SNAPTOGRID) != 0,
            ListOrigin(), new PixelSize(s.X, s.Y), new PixelSize(d.X, d.Y));
    });

    public bool IconsVisible() => WithView(() => _listView == IntPtr.Zero || IsWindowVisible(_listView));

    public void SetIconsVisible(bool visible) => WithView(() =>
    {
        if (_listView == IntPtr.Zero || IsWindowVisible(_listView) == visible) return true;
        // The desktop's "Show desktop icons" command is a toggle; only send it when the state differs.
        SendMessage(GetParent(_listView), WM_COMMAND, new IntPtr(ToggleDesktopIcons), IntPtr.Zero);
        return true;
    });

    private const int ToggleDesktopIcons = 0x7402;

    public void Reset() => Release();

    public void Dispose() => Release();

    // ── Plumbing ─────────────────────────────────────────────────────────

    private T WithView<T>(Func<T> action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Connect();
                return action();
            }
            catch (Exception ex) when (attempt == 0 && ex is COMException or InvalidComObjectException)
            {
                // Usually Explorer restarted and the proxy is dead: reconnect once.
                JobRunner.Log($"Desktop: shell view lost ({ex.Message}), reconnecting");
                Release();
            }
        }
    }

    private void Connect()
    {
        if (_view is not null) return;

        // Raymond Chen's recipe: ShellWindows → desktop browser → active view.
        var windows = (IShellWindows)new ShellWindowsClass();
        try
        {
            object loc = CSIDL_DESKTOP, root = null!;
            int hr = windows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out var dispatch);
            if (hr != 0 || dispatch is null) throw new COMException("Desktop window not found", hr);

            var sid = SID_STopLevelBrowser;
            var iid = IID_IShellBrowser;
            hr = ((IOleServiceProvider)dispatch).QueryService(ref sid, ref iid, out var browserObj);
            if (hr != 0 || browserObj is null) throw new COMException("Desktop browser not available", hr);

            ((IShellBrowser)browserObj).QueryActiveShellView(out var viewObj);
            _view = (IFolderView2)viewObj;

            var folderIid = IID_IShellFolder;
            _view.GetFolder(ref folderIid, out var folderObj);
            _folder = (IShellFolder)folderObj;

            ((IShellView)viewObj).GetWindow(out var defView);
            _listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }
        finally
        {
            Marshal.FinalReleaseComObject(windows);
        }
    }

    private void Release()
    {
        try { if (_folder is not null) Marshal.FinalReleaseComObject(_folder); } catch { }
        try { if (_view is not null) Marshal.FinalReleaseComObject(_view); } catch { }
        _folder = null;
        _view = null;
        _listView = IntPtr.Zero;
    }

    /// <summary>
    /// Screen position of the list view's client origin. Item positions are relative to it.
    /// TODO(verify): P2 — on multi-monitor setups this should equal the virtual-screen origin.
    /// </summary>
    private PixelPoint ListOrigin()
    {
        var p = new POINT();
        if (_listView != IntPtr.Zero && ClientToScreen(_listView, ref p)) return new PixelPoint(p.X, p.Y);
        return new PixelPoint(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN));
    }

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;

    /// <summary>Every item's child PIDL (caller frees it) with its parent-relative parsing name.</summary>
    private IEnumerable<(IntPtr Pidl, string Name)> Pidls()
    {
        _view!.ItemCount(SVGIO_ALLVIEW, out int count);
        for (int i = 0; i < count; i++)
        {
            _view.Item(i, out var pidl);
            if (pidl == IntPtr.Zero) continue;
            string? name = null;
            try { name = NameOf(pidl); }
            catch (Exception ex) { JobRunner.Log($"Desktop: unnamed item skipped: {ex.Message}"); }
            if (string.IsNullOrEmpty(name)) { Marshal.FreeCoTaskMem(pidl); continue; }
            yield return (pidl, name);
        }
    }

    private string? NameOf(IntPtr pidl)
    {
        _folder!.GetDisplayNameOf(pidl, SHGDN_INFOLDER | SHGDN_FORPARSING, out var strret);
        if (StrRetToStrW(ref strret, pidl, out var text) != 0) return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    private ItemKind KindOf(IntPtr pidl)
    {
        uint attrs = SFGAO_LINK | SFGAO_FOLDER | SFGAO_STREAM;
        try { _folder!.GetAttributesOf(1, [pidl], ref attrs); }
        catch { return ItemKind.File; }
        if ((attrs & SFGAO_LINK) != 0) return ItemKind.Shortcut;
        // ZIP files report FOLDER too, but they are also streams (real files).
        if ((attrs & SFGAO_FOLDER) != 0 && (attrs & SFGAO_STREAM) == 0) return ItemKind.Folder;
        return ItemKind.File;
    }
}
