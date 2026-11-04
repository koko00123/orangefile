using Orange.Core;
using Orange.Desktop.Layout;
using Orange.Desktop.Model;
using static Orange.Input.Native;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Orange.UI.Desktop;

/// <summary>
/// The background of one box: a plain Win32 window painted with GDI (no WPF), repainted only when
/// Windows asks. In Wallpaper mode it lives behind the icons; in Frame mode it is a frame with a hole.
/// </summary>
internal sealed class BoxWindow : Forms.NativeWindow, IDisposable
{
    private readonly DesktopRenderMode _mode;
    private Box _box;
    private PixelRect _rect;
    private int _dpi;
    private int _overflow;

    public BoxWindow(DesktopRenderMode mode, IntPtr host, PixelPoint hostOrigin, Box box, PixelRect rect, int dpi, int overflow)
    {
        _mode = mode;
        (_box, _rect, _dpi, _overflow) = (box, rect, dpi, overflow);

        var cp = new Forms.CreateParams { Caption = "Orange box", Width = rect.W, Height = rect.H, Parent = host };
        if (mode == DesktopRenderMode.Wallpaper)
        {
            // Child of the wallpaper layer: coordinates are relative to its client area.
            cp.Style = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
            cp.X = rect.X - hostOrigin.X;
            cp.Y = rect.Y - hostOrigin.Y;
        }
        else
        {
            // Owned by Progman: stays just above the desktop and survives "Show desktop".
            // TODO(verify): P5 — Win+D behaviour on the user's build.
            cp.Style = WS_POPUP | WS_VISIBLE;
            cp.ExStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            cp.X = rect.X;
            cp.Y = rect.Y;
        }
        CreateHandle(cp);
        if (mode == DesktopRenderMode.Frame) ApplyFrameRegion();
    }

    public string BoxId => _box.Id;

    /// <summary>New title, color or overflow count: repaint only (same position).</summary>
    public void Update(Box box, int overflow)
    {
        if (box == _box && overflow == _overflow) return;
        (_box, _overflow) = (box, overflow);
        if (Handle != IntPtr.Zero && IsWindow(Handle)) InvalidateRect(Handle, IntPtr.Zero, false);
    }

    public static int TitleHeight(int dpi) => 28 * dpi / 96;
    public static int Padding(int dpi) => 4 * dpi / 96;
    private static int Border(int dpi) => Math.Max(2, 2 * dpi / 96);

    /// <summary>Region = whole window minus the inside, so the real desktop icons show through.</summary>
    private void ApplyFrameRegion()
    {
        int b = Border(_dpi), t = TitleHeight(_dpi);
        var outer = CreateRectRgn(0, 0, _rect.W, _rect.H);
        var inner = CreateRectRgn(b, t, _rect.W - b, _rect.H - b);
        try
        {
            CombineRgn(outer, outer, inner, RGN_DIFF);
            if (SetWindowRgn(Handle, outer, true) != 0) outer = IntPtr.Zero;   // now owned by Windows
        }
        finally
        {
            if (outer != IntPtr.Zero) DeleteObject(outer);
            DeleteObject(inner);
        }
    }

    protected override void WndProc(ref Forms.Message m)
    {
        switch (m.Msg)
        {
            case WM_ERASEBKGND:
                m.Result = new IntPtr(1);   // everything is painted in WM_PAINT; avoids flicker
                return;
            case WM_PAINT:
                Paint();
                m.Result = IntPtr.Zero;
                return;
            case WM_MOUSEACTIVATE:
                m.Result = new IntPtr(MA_NOACTIVATE);
                return;
        }
        base.WndProc(ref m);
    }

    private void Paint()
    {
        var hdc = BeginPaint(Handle, out var ps);
        try
        {
            using var g = Drawing.Graphics.FromHdc(hdc);
            var fill = ParseColor(_box.Color, Drawing.Color.FromArgb(0x2B, 0x2B, 0x2B));
            var text = ParseColor(_box.TitleColor, Drawing.Color.White);
            int t = TitleHeight(_dpi);

            using (var bg = new Drawing.SolidBrush(fill))
                g.FillRectangle(bg, 0, 0, _rect.W, _rect.H);
            using (var bar = new Drawing.SolidBrush(Shade(fill, 0.82)))
                g.FillRectangle(bar, 0, 0, _rect.W, t);

            using var font = new Drawing.Font("Segoe UI Semibold", 13f * _dpi / 96, Drawing.GraphicsUnit.Pixel);
            int pad = 10 * _dpi / 96;
            var title = _overflow > 0 ? $"{_box.Title}   +{_overflow}" : _box.Title;
            Forms.TextRenderer.DrawText(g, title, font, new Drawing.Rectangle(pad, 0, _rect.W - 2 * pad, t), text,
                Forms.TextFormatFlags.Left | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.EndEllipsis
                | Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.NoPrefix);
        }
        catch (Exception ex)
        {
            JobRunner.Log($"Desktop: paint failed: {ex.Message}");
        }
        finally
        {
            EndPaint(Handle, ref ps);
        }
    }

    private static Drawing.Color ParseColor(string hex, Drawing.Color fallback)
    {
        try { return Drawing.ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }

    private static Drawing.Color Shade(Drawing.Color c, double f)
    {
        // Darken light colors and lighten dark ones, so the title bar always stands out a little.
        bool dark = c.R * 0.299 + c.G * 0.587 + c.B * 0.114 < 96;
        int Mix(int v) => dark ? (int)(v + (255 - v) * (1 - f)) : (int)(v * f);
        return Drawing.Color.FromArgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
