using System.Windows.Threading;
using Orange.Core;
using Orange.Desktop.Engine;
using static Orange.Input.Native;
using Forms = System.Windows.Forms;

namespace Orange.UI.Desktop;

/// <summary>
/// Turns Windows events into engine work (SPEC §11). No polling: file watchers, one hidden window
/// for broadcast messages, and the drop notice from DragWatcher. Every source is debounced, so a burst
/// (copying 50 files, a monitor waking up) costs one refresh.
/// </summary>
internal sealed class DesktopEvents : Forms.NativeWindow, IDisposable
{
    private readonly DesktopEngine _engine;
    private readonly Func<bool> _paused;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly List<(string Old, string New)> _renames = [];
    private readonly DispatcherTimer _files, _drop, _display, _explorer;
    private readonly uint _taskbarCreated;

    public DesktopEvents(DesktopEngine engine, Func<bool> paused)
    {
        _engine = engine;
        _paused = paused;
        _files = Timer(700, () => Run(() => _engine.Refresh(userDrop: false, TakeRenames()), "refresh after file changes"));
        _drop = Timer(300, () => Run(() => _engine.Refresh(userDrop: true, TakeRenames()), "refresh after a drop"));
        _display = Timer(1000, () => Run(_engine.Apply, "redraw after a display change"));
        _explorer = Timer(2000, () => Run(() => { _engine.Reconnect(); _engine.Apply(); }, "redraw after Explorer restarted"));
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    }

    public void Start()
    {
        // A hidden top-level window: message-only windows do not get WM_DISPLAYCHANGE or TaskbarCreated.
        CreateHandle(new Forms.CreateParams { Caption = "Orange desktop events", Style = WS_POPUP, ExStyle = WS_EX_TOOLWINDOW });

        Watch(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Watch(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
    }

    /// <summary>From DragWatcher (already off the hook thread). Only drops onto the desktop icon view count.</summary>
    public void OnLeftButtonUp(int x, int y)
    {
        if (_paused()) return;
        var hwnd = WindowFromPoint(new POINT { X = x, Y = y });
        if (ClassOf(hwnd) != "SysListView32" || ClassOf(GetParent(hwnd)) != "SHELLDLL_DefView") return;
        Restart(_drop);   // Explorer applies the drop a moment after the button goes up
    }

    private void Watch(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try
        {
            var w = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
            };
            // Watcher events arrive on pool threads; hop to the UI thread before touching anything.
            w.Created += (_, _) => Post(() => Restart(_files));
            w.Deleted += (_, _) => Post(() => Restart(_files));
            w.Renamed += (_, e) => Post(() => { _renames.Add((e.OldName ?? "", e.Name ?? "")); Restart(_files); });
            w.Error += (_, e) => JobRunner.Log($"Desktop: file watcher error in {folder}: {e.GetException().Message}");
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
        catch (Exception ex) { JobRunner.Log($"Desktop: cannot watch {folder}: {ex.Message}"); }
    }

    protected override void WndProc(ref Forms.Message m)
    {
        if (m.Msg == WM_DISPLAYCHANGE || (m.Msg == WM_SETTINGCHANGE && m.WParam.ToInt32() == SPI_SETWORKAREA))
            Restart(_display);
        else if (_taskbarCreated != 0 && m.Msg == (int)_taskbarCreated)
            Restart(_explorer);
        base.WndProc(ref m);
    }

    private List<(string, string)> TakeRenames()
    {
        var list = _renames.ToList();
        _renames.Clear();
        return list;
    }

    private void Run(Action action, string what)
    {
        // Edit mode owns the boxes; catch up once it closes (Done applies everything).
        if (_paused()) return;
        try { action(); }
        catch (Exception ex) { JobRunner.Log($"Desktop: could not {what}: {ex}"); }
    }

    private static DispatcherTimer Timer(int ms, Action tick)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); tick(); };
        return t;
    }

    private static void Restart(DispatcherTimer t)
    {
        t.Stop();
        t.Start();
    }

    private static void Post(Action a) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(a);

    public void Dispose()
    {
        foreach (var t in new[] { _files, _drop, _display, _explorer }) t.Stop();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
