using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Orange.Core;
using Orange.Desktop.Layout;
using Orange.Desktop.Model;
using static Orange.Input.Native;

namespace Orange.UI.Desktop;

/// <summary>
/// A box while editing: a normal window above the desktop that can be dragged by its title and
/// resized from its edges. Positions are physical pixels, set and read through the HWND so
/// monitors with different scales stay exact.
/// </summary>
internal sealed class EditBoxWindow : Window
{
    public const double TitleDip = 28;   // matches BoxWindow.TitleHeight at 96 DPI

    private readonly Border _frame;
    private readonly Border _bar;
    private readonly TextBlock _title;
    private readonly TextBlock _hint;
    private PixelRect _rect;
    private IntPtr _hwnd;

    public EditBoxWindow(Box box, PixelRect rect, int iconCount)
    {
        Box = box;
        _rect = rect;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Background = Brushes.Transparent;
        MinWidth = 90;
        MinHeight = 70;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Native move/resize: the title acts as a caption and the edges as resize borders.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = TitleDip,
            ResizeBorderThickness = new Thickness(8),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        _title = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(10, 0, 4, 0),
        };
        var menu = new Button
        {
            Content = "⋯", Width = 34, FontSize = 16, Padding = new Thickness(0, -4, 0, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White,
            Cursor = Cursors.Hand, ToolTip = Loc.T("Box options"),
        };
        WindowChrome.SetIsHitTestVisibleInChrome(menu, true);
        menu.Click += (_, _) => MenuRequested?.Invoke(this, menu);

        var barPanel = new DockPanel();
        DockPanel.SetDock(menu, Dock.Right);
        barPanel.Children.Add(menu);
        barPanel.Children.Add(_title);
        _bar = new Border { Height = TitleDip, Child = barPanel };

        _hint = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10),
        };

        var body = new DockPanel();
        DockPanel.SetDock(_bar, Dock.Top);
        body.Children.Add(_bar);
        body.Children.Add(_hint);

        _frame = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1F)),   // Orange accent: "you are editing"
            Child = body,
        };
        Content = _frame;
        _frame.MouseRightButtonUp += (_, e) => { MenuRequested?.Invoke(this, _frame); e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) EscapePressed?.Invoke(); };

        Update(box, iconCount);
    }

    public Box Box { get; private set; }

    /// <summary>Raised when the user finished dragging or resizing, with the new physical rect.</summary>
    public event Action<EditBoxWindow, PixelRect>? Moved;
    public event Action<EditBoxWindow, FrameworkElement>? MenuRequested;
    public event Action? EscapePressed;

    public void Update(Box box, int iconCount)
    {
        Box = box;
        _title.Text = box.Title;
        var c = ParseColor(box.Color);
        // See-through while editing, so the icons underneath stay visible.
        _frame.Background = new SolidColorBrush(Color.FromArgb(0xB4, c.R, c.G, c.B));
        _bar.Background = new SolidColorBrush(Color.FromArgb(0xE6, c.R, c.G, c.B));
        _hint.Text = iconCount == 0
            ? Loc.T("Place the box over icons and press Done")
            : Loc.F("{0} icons", iconCount);
    }

    public void Place(PixelRect rect)
    {
        _rect = rect;
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, IntPtr.Zero, rect.X, rect.Y, rect.W, rect.H, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
        Place(_rect);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_EXITSIZEMOVE && GetWindowRect(hwnd, out var r))
        {
            var rect = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            if (rect != _rect)
            {
                _rect = rect;
                Moved?.Invoke(this, rect);
            }
        }
        return IntPtr.Zero;
    }

    public static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0x2B, 0x2B, 0x2B); }
    }
}
