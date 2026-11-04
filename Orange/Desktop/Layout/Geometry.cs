using Orange.Desktop.Model;

namespace Orange.Desktop.Layout;

// Physical pixels in virtual-screen coordinates (Orange is PerMonitorV2), so rects on monitors
// left of or above the primary one have negative X/Y.

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelSize(int W, int H);

public readonly record struct PixelRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public PixelPoint Center => new(X + W / 2, Y + H / 2);

    public bool Contains(PixelPoint p) => p.X >= X && p.X < Right && p.Y >= Y && p.Y < Bottom;

    /// <summary>Squared distance from a point to the rect (0 when inside), for "nearest monitor".</summary>
    public long DistanceSquared(PixelPoint p)
    {
        long dx = p.X < X ? X - p.X : p.X >= Right ? p.X - (Right - 1) : 0;
        long dy = p.Y < Y ? Y - p.Y : p.Y >= Bottom ? p.Y - (Bottom - 1) : 0;
        return dx * dx + dy * dy;
    }
}

/// <summary>A connected monitor. WorkArea excludes the taskbar. Dpi is 96 at 100 % scale.</summary>
public sealed record MonitorInfo(MonitorKey Key, PixelRect Bounds, PixelRect WorkArea, int Dpi, bool IsPrimary);

/// <summary>An icon on the desktop as the shell reports it. Position is the item's top-left corner.</summary>
public sealed record DesktopItem(string Name, ItemKind Kind, PixelPoint Position);
