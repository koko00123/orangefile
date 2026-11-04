using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Orange.Core;
using Orange.Desktop.Engine;
using Orange.Desktop.Layout;
using Orange.Desktop.Model;
using static Orange.Input.Native;

namespace Orange.UI.Desktop;

/// <summary>
/// Edit mode (SPEC §10): every box becomes a movable window, plus a small toolbar with
/// "New box" and "Done". Each change is saved right away; Done redraws the boxes behind the icons.
/// </summary>
internal sealed class EditSession
{
    private static readonly string[] Palette =
        ["#2B2B2B", "#1E3A5F", "#3B2A4A", "#1F3D2B", "#4A1F24", "#3A4250", "#6B5A3A", "#B35A12"];

    private readonly DesktopEngine _engine;
    private readonly Dictionary<string, EditBoxWindow> _windows = [];
    private Window? _toolbar;
    private bool _closing;

    public EditSession(DesktopEngine engine) => _engine = engine;

    public bool IsOpen { get; private set; }
    public event Action? Closed;

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        _engine.HideBoxes();
        var map = _engine.Map();
        foreach (var box in _engine.Layout.Boxes)
            if (map.ToPixels(box) is { } rect) AddWindow(box, rect);
        ShowToolbar(map);
    }

    public void Close()
    {
        if (!IsOpen || _closing) return;
        _closing = true;
        foreach (var w in _windows.Values) w.Close();
        _windows.Clear();
        _toolbar?.Close();
        _toolbar = null;

        try
        {
            int adopted = _engine.AdoptFreeIcons();
            if (adopted > 0) JobRunner.Log($"Desktop: {adopted} icons joined boxes");
            _engine.Apply();
        }
        catch (Exception ex) { JobRunner.Log($"Desktop: could not finish editing: {ex}"); }

        IsOpen = false;
        _closing = false;
        Closed?.Invoke();
    }

    // ── Boxes ────────────────────────────────────────────────────────────

    private void AddWindow(Box box, PixelRect rect)
    {
        var w = new EditBoxWindow(box, rect, _engine.CountItems(box));
        w.Moved += OnMoved;
        w.MenuRequested += ShowMenu;
        w.EscapePressed += Close;
        w.Closed += (_, _) => { if (!_closing) _windows.Remove(w.Box.Id); };
        _windows[box.Id] = w;
        w.Show();
    }

    private void OnMoved(EditBoxWindow w, PixelRect rect)
    {
        var map = _engine.Map();
        if (map.ToRelative(rect) is not { } stored) return;
        var (monitor, rel) = stored;
        var box = w.Box with { Monitor = monitor, Rect = rel };
        Save(box);
        // The stored rect is clamped to the monitor; show what was kept.
        if (map.ToPixels(box) is { } kept && kept != rect) w.Place(kept);
        _engine.ArrangeBox(box);   // icons follow the box
    }

    private void NewBox()
    {
        var map = _engine.Map();
        GetCursorPos(out var cursor);
        var monitor = map.MonitorAt(new PixelPoint(cursor.X, cursor.Y)) ?? map.Primary;
        if (monitor is null) return;

        var layout = _engine.Layout;
        var box = new Box
        {
            Id = Box.NewId(),
            Title = Loc.T("New box"),
            Color = Palette[layout.Boxes.Count % Palette.Length],
            Monitor = monitor.Key,
            Rect = new RelRect(0.4, 0.3, 0.2, 0.3),
        };
        _engine.SetLayout(layout with { Boxes = [.. layout.Boxes, box] });
        if (map.ToPixels(box) is { } rect) AddWindow(box, rect);
    }

    private void Save(Box box)
    {
        var layout = _engine.Layout;
        _engine.SetLayout(layout with { Boxes = layout.Boxes.Select(b => b.Id == box.Id ? box : b).ToList() });
        if (_windows.TryGetValue(box.Id, out var w)) w.Update(box, _engine.CountItems(box));
    }

    private void Delete(EditBoxWindow w)
    {
        var box = w.Box;
        var answer = MessageBox.Show(w, Loc.F("Delete the box \"{0}\"? Its icons stay on the desktop.", box.Title),
            "Orange", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        var layout = _engine.Layout;
        _engine.SetLayout(layout with { Boxes = layout.Boxes.Where(b => b.Id != box.Id).ToList() });
        _windows.Remove(box.Id);
        w.Close();
    }

    // ── Box menu ─────────────────────────────────────────────────────────

    private void ShowMenu(EditBoxWindow w, FrameworkElement anchor)
    {
        var box = w.Box;
        var menu = new ContextMenu { PlacementTarget = anchor };

        menu.Items.Add(Item(Loc.T("Rename…"), () =>
        {
            var name = PromptWindow.Ask("Rename box", "Box name:", box.Title);
            if (name is not null) Save(w.Box with { Title = name });
        }));

        var colors = new MenuItem { Header = Loc.T("Color") };
        foreach (var hex in Palette)
        {
            var swatch = new Rectangle { Width = 64, Height = 16, Fill = new SolidColorBrush(EditBoxWindow.ParseColor(hex)) };
            var item = new MenuItem { Header = swatch, IsChecked = string.Equals(box.Color, hex, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => Save(w.Box with { Color = hex });
            colors.Items.Add(item);
        }
        menu.Items.Add(colors);

        var sort = new MenuItem { Header = Loc.T("Sort by") };
        foreach (var (mode, label) in new[] { (BoxSort.Name, "Name"), (BoxSort.Type, "Type"), (BoxSort.Manual, "Manual") })
        {
            var item = new MenuItem { Header = Loc.T(label), IsChecked = box.Sort == mode };
            item.Click += (_, _) => { Save(w.Box with { Sort = mode }); _engine.ArrangeBox(w.Box); };
            sort.Items.Add(item);
        }
        menu.Items.Add(sort);

        menu.Items.Add(Item(Loc.T("Rule: extensions…"), () =>
        {
            var current = string.Join("; ", w.Box.Rules.OfType<ExtensionRule>().Select(r => r.Value));
            var value = PromptWindow.Ask("Box rule", "Extensions that go into this box automatically (e.g. .pdf; .docx):", current);
            if (value is null) return;
            var others = w.Box.Rules.Where(r => r is not ExtensionRule);
            Save(w.Box with { Rules = [.. others, new ExtensionRule(value)] });
        }));
        if (box.Rules.Count > 0)
            menu.Items.Add(Item(Loc.T("Remove rules"), () => Save(w.Box with { Rules = [] })));

        var map = _engine.Map();
        if (map.Monitors.Count > 1)
        {
            var move = new MenuItem { Header = Loc.T("Move to monitor") };
            for (int i = 0; i < map.Monitors.Count; i++)
            {
                var m = map.Monitors[i];
                var item = new MenuItem { Header = $"{i + 1} · {m.Key.Name}", IsChecked = m.Key.Id == box.Monitor.Id };
                item.Click += (_, _) =>
                {
                    var moved = w.Box with { Monitor = m.Key };
                    Save(moved);
                    if (_engine.Map().ToPixels(moved) is { } rect) w.Place(rect);
                    _engine.ArrangeBox(moved);
                };
                move.Items.Add(item);
            }
            menu.Items.Add(move);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Delete box"), () => Delete(w)));
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    // ── Toolbar ──────────────────────────────────────────────────────────

    private void ShowToolbar(MonitorMap map)
    {
        var bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
        var accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1F));
        Button MakeButton(string text, Action click, bool primary = false)
        {
            var b = new Button
            {
                Content = text, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4),
                Foreground = primary ? Brushes.Black : Brushes.White, Background = primary ? accent : bg,
                BorderBrush = accent, Cursor = Cursors.Hand,
            };
            b.Click += (_, _) => click();
            return b;
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 8, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Editing desktop · Esc to finish"), Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        panel.Children.Add(MakeButton(Loc.T("+ New box"), NewBox));
        panel.Children.Add(MakeButton(Loc.T("Done"), Close, primary: true));

        _toolbar = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight, Background = bg, BorderBrush = accent,
            BorderThickness = new Thickness(1), Content = panel, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        _toolbar.KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        _toolbar.Closed += (_, _) => { if (!_closing) Close(); };

        // Top center of the monitor under the cursor, in physical pixels.
        GetCursorPos(out var cursor);
        var area = (map.MonitorAt(new PixelPoint(cursor.X, cursor.Y)) ?? map.Primary)?.WorkArea ?? new PixelRect(0, 0, 800, 600);
        _toolbar.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(_toolbar).Handle;
            GetWindowRect(h, out var r);
            int w = r.Right - r.Left;
            SetWindowPos(h, IntPtr.Zero, area.X + (area.W - w) / 2, area.Y + 12, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        };
        _toolbar.ContentRendered += (_, _) =>
        {
            // Re-center once the real size is known.
            var h = new WindowInteropHelper(_toolbar).Handle;
            GetWindowRect(h, out var r);
            SetWindowPos(h, IntPtr.Zero, area.X + (area.W - (r.Right - r.Left)) / 2, area.Y + 12, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        };
        _toolbar.Show();
        _toolbar.Activate();   // so Esc works right away
    }
}
