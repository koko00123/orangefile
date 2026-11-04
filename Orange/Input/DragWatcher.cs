using System.Runtime.InteropServices;
using System.Windows.Threading;
using Orange.Core;

namespace Orange.Input;

/// <summary>
/// Watches the global mouse and keyboard. When the left button is held and moved past the
/// system drag threshold (a drag is in progress) and a mode key is pressed, it asks for the wheel.
/// Only drags that start in an app from Settings.DragSourceApps (Explorer by default) are watched.
///   Shift → Convert, Ctrl → Tools, Alt → Actions, Esc → cancel.
/// </summary>
public sealed class DragWatcher : IDisposable
{
    public event Action<WheelMode, int, int>? WheelRequested;
    public event Action<WheelMode>? ModeChanged;
    public event Action? DragEnded;
    public event Action? CancelRequested;
    public event Action<int, int>? MouseDown;

    /// <summary>Left button released anywhere (the Desktop module checks whether an icon was dropped).</summary>
    public event Action<int, int>? LeftButtonUp;

    private readonly Native.HookProc _mouseProc;
    private readonly Native.HookProc _keyProc;
    private readonly Dispatcher _dispatcher;
    private IntPtr _mouseHook, _keyHook;

    private bool _buttonDown, _dragging, _wheelShown;
    private Native.POINT _downPt;
    private WheelMode? _lastMode;

    public DragWatcher()
    {
        // Keep the delegates alive as fields so the GC doesn't collect them while hooked.
        _mouseProc = MouseHook;
        _keyProc = KeyHook;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void Start()
    {
        var hMod = Native.GetModuleHandle(null);
        _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, hMod, 0);
        _keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyProc, hMod, 0);
        if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
            throw new InvalidOperationException($"Could not install input hooks (error {Marshal.GetLastWin32Error()}).");
    }

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case Native.WM_LBUTTONDOWN:
                case Native.WM_RBUTTONDOWN:
                    if ((int)wParam == Native.WM_LBUTTONDOWN)
                    {
                        // Only drags that start in an allowed app (Explorer by default) count.
                        _buttonDown = IsAllowedSource(info.pt);
                        _dragging = false;
                        _downPt = info.pt;
                    }
                    Post(() => MouseDown?.Invoke(info.pt.X, info.pt.Y));
                    break;

                case Native.WM_MOUSEMOVE:
                    if (_buttonDown && !_dragging && PastThreshold(info.pt))
                    {
                        _dragging = true;
                        // The user may already be holding a mode key when the drag starts.
                        var held = HeldMode();
                        if (held is { } m) RequestWheel(m);
                    }
                    break;

                case Native.WM_LBUTTONUP:
                    bool wasDragging = _dragging;
                    _buttonDown = _dragging = _wheelShown = false;
                    _lastMode = null;
                    if (wasDragging) Post(() => DragEnded?.Invoke());
                    // Only queued when someone listens, so the hook stays free when the module is off.
                    if (LeftButtonUp is { } up) Post(() => up(info.pt.X, info.pt.Y));
                    break;
            }
        }
        return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
            {
                var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)info.vkCode;

                if (vk == Native.VK_ESCAPE)
                {
                    _wheelShown = false;
                    _lastMode = null;
                    Post(() => CancelRequested?.Invoke());
                }
                else if (ModeFor(vk) is { } mode)
                {
                    if (_dragging) RequestWheel(mode);
                    else Post(() => ModeChanged?.Invoke(mode)); // lets a click-opened wheel switch modes
                }
            }
        }
        return Native.CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    private void RequestWheel(WheelMode mode)
    {
        if (mode == _lastMode) return; // ignore key auto-repeat
        _lastMode = mode;

        if (!_wheelShown)
        {
            _wheelShown = true;
            Native.GetCursorPos(out var p);
            Post(() => WheelRequested?.Invoke(mode, p.X, p.Y));
        }
        else
        {
            Post(() => ModeChanged?.Invoke(mode));
        }
    }

    private bool PastThreshold(Native.POINT p)
    {
        int cx = Math.Max(4, Native.GetSystemMetrics(Native.SM_CXDRAG));
        int cy = Math.Max(4, Native.GetSystemMetrics(Native.SM_CYDRAG));
        return Math.Abs(p.X - _downPt.X) > cx || Math.Abs(p.Y - _downPt.Y) > cy;
    }

    // Which key opens which mode comes from Settings (default Shift / Ctrl / Alt).
    private static WheelMode? ModeFor(int vk)
    {
        ModKey? key = vk switch
        {
            Native.VK_SHIFT or Native.VK_LSHIFT or Native.VK_RSHIFT => ModKey.Shift,
            Native.VK_CONTROL or Native.VK_LCONTROL or Native.VK_RCONTROL => ModKey.Ctrl,
            Native.VK_MENU or Native.VK_LMENU or Native.VK_RMENU => ModKey.Alt,
            _ => null
        };
        return key is { } k ? Settings.Current.ModeFor(k) : null;
    }

    private static bool IsAllowedSource(Native.POINT p) =>
        Native.ProcessNameAt(p) is { } name &&
        Settings.Current.DragSourceApps.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static WheelMode? HeldMode()
    {
        if (Native.IsDown(Native.VK_SHIFT)) return Settings.Current.ModeFor(ModKey.Shift);
        if (Native.IsDown(Native.VK_CONTROL)) return Settings.Current.ModeFor(ModKey.Ctrl);
        if (Native.IsDown(Native.VK_MENU)) return Settings.Current.ModeFor(ModKey.Alt);
        return null;
    }

    // Hook callbacks must return fast, so UI work is queued on the dispatcher.
    private void Post(Action a) => _dispatcher.BeginInvoke(a);

    public void Dispose()
    {
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyHook);
        _mouseHook = _keyHook = IntPtr.Zero;
    }
}
