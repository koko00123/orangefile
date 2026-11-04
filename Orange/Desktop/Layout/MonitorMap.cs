using Orange.Core;
using Orange.Desktop.Model;

namespace Orange.Desktop.Layout;

/// <summary>Converts box positions between monitor fractions (stored) and virtual-screen pixels (drawn).</summary>
public sealed class MonitorMap
{
    private readonly DesktopOrphanBoxes _orphans;

    public MonitorMap(IReadOnlyList<MonitorInfo> monitors, DesktopOrphanBoxes orphans = DesktopOrphanBoxes.Hide)
    {
        // Numbered 1, 2, 3… from left to right (then top to bottom), as the user sees them on the desk.
        Monitors = monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
        _orphans = orphans;
    }

    /// <summary>Connected monitors, ordered left to right.</summary>
    public IReadOnlyList<MonitorInfo> Monitors { get; }

    public MonitorInfo? Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();

    public MonitorInfo? Find(MonitorKey key) =>
        string.IsNullOrEmpty(key.Id) ? null : Monitors.FirstOrDefault(m => m.Key.Id == key.Id);

    /// <summary>1-based number of a monitor (left to right), or 0 if it is not connected.</summary>
    public int NumberOf(MonitorKey key)
    {
        var m = Find(key);
        return m is null ? 0 : Monitors.ToList().IndexOf(m) + 1;
    }

    /// <summary>The monitor whose bounds contain the point, else the nearest one.</summary>
    public MonitorInfo? MonitorAt(PixelPoint p) =>
        Monitors.FirstOrDefault(m => m.Bounds.Contains(p)) ?? Monitors.MinBy(m => m.Bounds.DistanceSquared(p));

    /// <summary>Where a box is drawn, or null when its monitor is gone and orphans are hidden.</summary>
    public PixelRect? ToPixels(Box box)
    {
        var monitor = Find(box.Monitor) ?? (_orphans == DesktopOrphanBoxes.MoveToPrimary ? Primary : null);
        return monitor is null ? null : ToPixels(box.Rect, monitor.WorkArea);
    }

    public static PixelRect ToPixels(RelRect r, PixelRect area) => new(
        area.X + (int)Math.Round(r.X * area.W),
        area.Y + (int)Math.Round(r.Y * area.H),
        Math.Max(1, (int)Math.Round(r.W * area.W)),
        Math.Max(1, (int)Math.Round(r.H * area.H)));

    /// <summary>
    /// Stores a box drawn at <paramref name="rect"/>: it belongs to the monitor holding its center,
    /// and is clamped into that monitor's work area.
    /// </summary>
    public (MonitorKey Key, RelRect Rect)? ToRelative(PixelRect rect)
    {
        var monitor = MonitorAt(rect.Center);
        if (monitor is null) return null;
        var a = monitor.WorkArea;
        double w = Math.Clamp((double)rect.W / a.W, 0, 1);
        double h = Math.Clamp((double)rect.H / a.H, 0, 1);
        double x = Math.Clamp((double)(rect.X - a.X) / a.W, 0, 1 - w);
        double y = Math.Clamp((double)(rect.Y - a.Y) / a.H, 0, 1 - h);
        return (monitor.Key, new RelRect(x, y, w, h));
    }
}
