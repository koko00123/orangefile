using Orange.Core;
using Orange.Desktop.Layout;
using Orange.Desktop.Model;
using Orange.Desktop.Shell;
using Orange.Desktop.Store;
using Orange.UI.Desktop;

namespace Orange.Desktop.Engine;

/// <summary>
/// Owns the desktop config, draws the boxes of the active layout and puts their icons inside.
/// Runs on the UI thread. Events (new files, monitor changes, Explorer restarts) come in T6.
/// </summary>
internal sealed class DesktopEngine : IDisposable
{
    public const string DefaultLayoutName = "Default";

    private readonly IDesktopIcons _icons;
    private readonly IMonitors _monitors;
    private readonly DesktopStore _store;
    private readonly List<BoxWindow> _windows = [];

    public DesktopEngine(IDesktopIcons icons, IMonitors monitors, DesktopStore store)
    {
        (_icons, _monitors, _store) = (icons, monitors, store);
    }

    public DesktopConfig Config { get; private set; } = DesktopConfig.Empty;
    public DesktopRenderMode ActiveMode { get; private set; }
    public int BoxCount => _windows.Count;
    public int IconsMoved { get; private set; }

    /// <summary>The active layout; an empty "Default" one when nothing is configured yet.</summary>
    public DesktopLayout Layout => Config.Active ?? new DesktopLayout { Name = DefaultLayoutName };

    public void Start()
    {
        Config = _store.Load();
        Safe(Apply, "apply the layout");
    }

    public MonitorMap Map() => new(_monitors.GetMonitors(), Settings.Current.DesktopOrphanBoxes);

    public PixelSize Cell() => _icons.GetSpacing();

    /// <summary>Replaces the active layout (by name) and saves desktop.json.</summary>
    public void SetLayout(DesktopLayout layout)
    {
        var layouts = Config.Layouts.Any(l => l.Name == layout.Name)
            ? Config.Layouts.Select(l => l.Name == layout.Name ? layout : l).ToList()
            : Config.Layouts.Append(layout).ToList();
        Config = DesktopStore.Sanitize(Config with { ActiveLayout = layout.Name, Layouts = layouts });
        _store.Save(Config);
    }

    /// <summary>Draws every box of the active layout and arranges all their icons.</summary>
    public void Apply()
    {
        HideBoxes();
        var layout = Config.Active;
        if (layout is null || layout.Boxes.Count == 0) return;

        if (_icons.EnsureFreePositioning())
            JobRunner.Log("Desktop: turned off auto arrange / align to grid");

        var map = Map();
        var cell = Cell();
        var onDesktop = ItemsByName();
        var (mode, host, origin) = Host(Settings.Current.DesktopRenderMode);
        ActiveMode = mode;

        var moves = new List<(string, PixelPoint)>();
        foreach (var box in layout.Boxes)
        {
            if (Place(box, map, cell, onDesktop) is not { } placed) continue;   // monitor not connected
            var (rect, dpi, placement) = placed;
            moves.AddRange(placement.Placed);
            try { _windows.Add(new BoxWindow(mode, host, origin, box, rect, dpi, placement.Overflow.Count)); }
            catch (Exception ex) { JobRunner.Log($"Desktop: could not create box '{box.Title}': {ex.Message}"); }
        }
        IconsMoved = _icons.SetPositions(moves);
    }

    /// <summary>Removes the box backgrounds (icons stay where they are). Used while editing.</summary>
    public void HideBoxes()
    {
        foreach (var w in _windows) w.Dispose();
        _windows.Clear();
    }

    /// <summary>Moves one box's icons into its grid. Returns the number that did not fit.</summary>
    public int ArrangeBox(Box box)
    {
        _icons.EnsureFreePositioning();
        var onDesktop = ItemsByName();
        if (Place(box, Map(), Cell(), onDesktop) is not { } placed) return 0;
        _icons.SetPositions(placed.Placement.Placed);
        return placed.Placement.Overflow.Count;
    }

    /// <summary>How many of the box's items are on the desktop right now.</summary>
    public int CountItems(Box box)
    {
        var onDesktop = ItemsByName();
        return box.Items.Count(onDesktop.ContainsKey);
    }

    /// <summary>
    /// Icons with no box (and not pulled out on purpose) whose center lies inside a box join it.
    /// Lets the user drop a new box over icons, or drag icons in, and keep them there.
    /// </summary>
    public int AdoptFreeIcons()
    {
        var layout = Layout;
        if (layout.Boxes.Count == 0) return 0;
        var map = Map();
        var owners = Membership.OwnerOf(layout);
        var loose = new HashSet<string>(layout.LooseItems, StringComparer.OrdinalIgnoreCase);
        var free = ItemsByName().Values.Where(i => !owners.ContainsKey(i.Name) && !loose.Contains(i.Name)).ToList();

        var rects = layout.Boxes
            .Select(b => (b.Id, Rect: map.ToPixels(b)))
            .Where(b => b.Rect is not null)
            .Select(b => (b.Id, b.Rect!.Value))
            .ToList();
        var joined = Membership.FromPositions(rects, free, Cell())
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (joined.Count > 0) SetLayout(Membership.Reassign(layout, joined));
        return joined.Count;
    }

    private (PixelRect Rect, int Dpi, BoxPlacement Placement)? Place(Box box, MonitorMap map, PixelSize cell,
        IReadOnlyDictionary<string, DesktopItem> onDesktop)
    {
        if (map.ToPixels(box) is not { } rect) return null;
        int dpi = map.MonitorAt(rect.Center)?.Dpi ?? 96;
        var items = box.Items.Where(onDesktop.ContainsKey).Select(n => onDesktop[n]).ToList();
        var placement = BoxLayout.Place(rect, cell, BoxWindow.TitleHeight(dpi), BoxWindow.Padding(dpi), items, box.Sort, box.Items);
        return (rect, dpi, placement);
    }

    // The user's and the public desktop can both hold the same name; the first one wins.
    private Dictionary<string, DesktopItem> ItemsByName() => _icons.GetItems()
        .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

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

    private static void Safe(Action action, string what)
    {
        try { action(); }
        catch (Exception ex) { JobRunner.Log($"Desktop: could not {what}: {ex}"); }
    }

    public void Dispose()
    {
        HideBoxes();
        _icons.Dispose();
    }
}
