using Orange.Core;
using Orange.Desktop.Layout;
using Orange.Desktop.Model;
using Orange.Desktop.Shell;
using Orange.Desktop.Store;
using Orange.UI.Desktop;

namespace Orange.Desktop.Engine;

/// <summary>
/// Draws the boxes of the active layout and puts their icons inside. Runs on the UI thread.
/// T4 scope: apply once at start. Events (new files, monitor changes, Explorer restarts) come in T6.
/// </summary>
internal sealed class DesktopEngine : IDisposable
{
    private readonly IDesktopIcons _icons;
    private readonly IMonitors _monitors;
    private readonly DesktopStore _store;
    private readonly List<BoxWindow> _windows = [];

    public DesktopEngine(IDesktopIcons icons, IMonitors monitors, DesktopStore store)
    {
        (_icons, _monitors, _store) = (icons, monitors, store);
    }

    public DesktopRenderMode ActiveMode { get; private set; }
    public int BoxCount => _windows.Count;
    public int IconsMoved { get; private set; }

    public void Start()
    {
        try { Apply(); }
        catch (Exception ex) { JobRunner.Log($"Desktop: could not apply the layout: {ex}"); }
    }

    public void Apply()
    {
        CloseWindows();
        var layout = _store.Load().Active;
        if (layout is null || layout.Boxes.Count == 0) return;

        var settings = Settings.Current;
        var map = new MonitorMap(_monitors.GetMonitors(), settings.DesktopOrphanBoxes);
        if (_icons.EnsureFreePositioning())
            JobRunner.Log("Desktop: turned off auto arrange / align to grid");

        var cell = _icons.GetSpacing();
        // The user's and the public desktop can both hold the same name; the first one wins.
        var onDesktop = _icons.GetItems().GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var (mode, host, origin) = Host(settings.DesktopRenderMode);
        ActiveMode = mode;

        var moves = new List<(string, PixelPoint)>();
        foreach (var box in layout.Boxes)
        {
            if (map.ToPixels(box) is not { } rect) continue;   // monitor not connected
            int dpi = map.MonitorAt(rect.Center)?.Dpi ?? 96;

            var items = box.Items.Where(onDesktop.ContainsKey).Select(n => onDesktop[n]).ToList();
            var placement = BoxLayout.Place(rect, cell, BoxWindow.TitleHeight(dpi), BoxWindow.Padding(dpi),
                items, box.Sort, box.Items);
            moves.AddRange(placement.Placed);

            try { _windows.Add(new BoxWindow(mode, host, origin, box, rect, dpi, placement.Overflow.Count)); }
            catch (Exception ex) { JobRunner.Log($"Desktop: could not create box '{box.Title}': {ex.Message}"); }
        }
        IconsMoved = _icons.SetPositions(moves);
    }

    /// <summary>Wallpaper layer when available, else Frame mode on top of Progman.</summary>
    private static (DesktopRenderMode, IntPtr, PixelPoint) Host(DesktopRenderMode wanted)
    {
        if (wanted == DesktopRenderMode.Wallpaper)
        {
            var layer = DesktopLayer.FindWallpaperLayer();
            if (layer != IntPtr.Zero) return (DesktopRenderMode.Wallpaper, layer, DesktopLayer.ClientOrigin(layer));
            JobRunner.Log("Desktop: wallpaper layer not found, using Frame mode");
        }
        return (DesktopRenderMode.Frame, DesktopLayer.Progman, default);
    }

    private void CloseWindows()
    {
        foreach (var w in _windows) w.Dispose();
        _windows.Clear();
    }

    public void Dispose()
    {
        CloseWindows();
        _icons.Dispose();
    }
}
