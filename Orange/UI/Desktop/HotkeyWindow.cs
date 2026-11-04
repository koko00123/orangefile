using Orange.Core;
using Orange.Desktop.Engine;
using static Orange.Input.Native;
using Forms = System.Windows.Forms;

namespace Orange.UI.Desktop;

/// <summary>Message-only window that receives global hotkeys (RegisterHotKey, no keyboard hook).</summary>
internal sealed class HotkeyWindow : Forms.NativeWindow, IDisposable
{
    private readonly HashSet<int> _ids = [];

    /// <summary>Raised with the id given to <see cref="Register"/>.</summary>
    public event Action<int>? Pressed;

    /// <summary>Registers (or re-registers) one hotkey. False when the text is invalid or another program owns it.</summary>
    public bool Register(int id, string text)
    {
        Unregister(id);
        if (string.IsNullOrWhiteSpace(text)) return true;   // "no shortcut" is fine
        if (!Hotkey.TryParse(text, out var mods, out var vk))
        {
            JobRunner.Log($"Desktop: invalid hotkey '{text}'");
            return false;
        }
        if (Handle == IntPtr.Zero) CreateHandle(new Forms.CreateParams { Parent = HWND_MESSAGE });
        if (!RegisterHotKey(Handle, id, mods | MOD_NOREPEAT, vk))
        {
            JobRunner.Log($"Desktop: hotkey '{text}' is taken by another program");
            return false;
        }
        _ids.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_ids.Remove(id)) UnregisterHotKey(Handle, id);
    }

    public void UnregisterWhere(Func<int, bool> match)
    {
        foreach (var id in _ids.Where(match).ToList()) Unregister(id);
    }

    protected override void WndProc(ref Forms.Message m)
    {
        if (m.Msg == WM_HOTKEY) Pressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterWhere(_ => true);
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
