using Orange.Core;
using Orange.Desktop.Engine;
using static Orange.Input.Native;
using Forms = System.Windows.Forms;

namespace Orange.UI.Desktop;

/// <summary>Message-only window that receives one global hotkey (RegisterHotKey, no keyboard hook).</summary>
internal sealed class HotkeyWindow : Forms.NativeWindow, IDisposable
{
    private const int Id = 0x0D35;
    private bool _registered;

    public event Action? Pressed;

    public bool Register(string text)
    {
        if (!Hotkey.TryParse(text, out var mods, out var vk))
        {
            JobRunner.Log($"Desktop: invalid hotkey '{text}'");
            return false;
        }
        if (Handle == IntPtr.Zero) CreateHandle(new Forms.CreateParams { Parent = HWND_MESSAGE });
        _registered = RegisterHotKey(Handle, Id, mods | MOD_NOREPEAT, vk);
        if (!_registered) JobRunner.Log($"Desktop: hotkey '{text}' is taken by another program");
        return _registered;
    }

    protected override void WndProc(ref Forms.Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(Handle, Id);
        _registered = false;
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
