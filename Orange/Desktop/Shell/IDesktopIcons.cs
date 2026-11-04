using Orange.Desktop.Layout;

namespace Orange.Desktop.Shell;

/// <summary>The icons on the Windows desktop. Positions are virtual-screen pixels (top-left of each item).</summary>
public interface IDesktopIcons : IDisposable
{
    IReadOnlyList<DesktopItem> GetItems();

    /// <summary>Icon cell size (icon + label) the desktop uses between items.</summary>
    PixelSize GetSpacing();

    /// <summary>Moves items by name; unknown names are skipped. Returns how many were moved.</summary>
    int SetPositions(IReadOnlyList<(string Name, PixelPoint Position)> moves);

    /// <summary>Turns off "Auto arrange icons" and "Align icons to grid". Returns true if anything changed.</summary>
    bool EnsureFreePositioning();

    /// <summary>Puts "Auto arrange" and "Align to grid" back as they were (used by diagnostics).</summary>
    void RestoreArrangement(DesktopViewInfo before);

    /// <summary>Forgets the cached shell view; the next call reconnects (after an Explorer restart).</summary>
    void Reset();

    /// <summary>Details for diagnostics: flags, window origin.</summary>
    DesktopViewInfo GetViewInfo();
}

public sealed record DesktopViewInfo(bool AutoArrange, bool SnapToGrid, PixelPoint ListOrigin, PixelSize Spacing, PixelSize DefaultSpacing);

/// <summary>Connected monitors right now.</summary>
public interface IMonitors
{
    IReadOnlyList<MonitorInfo> GetMonitors();
}
