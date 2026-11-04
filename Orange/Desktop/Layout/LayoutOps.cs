using Orange.Desktop.Engine;
using Orange.Desktop.Model;

namespace Orange.Desktop.Layout;

/// <summary>
/// Named layouts (SPEC §3): save, switch, rename, delete, shortcut. Pure functions over the config;
/// each returns null when the change is not allowed (empty or taken name, last layout, bad shortcut).
/// </summary>
public static class LayoutOps
{
    public static DesktopConfig? Switch(DesktopConfig c, string name) =>
        Find(c, name) is { } l ? c with { ActiveLayout = l.Name } : null;

    /// <summary>Copies the active layout (boxes, rules, positions) under a new name and makes it active.</summary>
    public static DesktopConfig? SaveAs(DesktopConfig c, string name, string fallbackName = "Default")
    {
        name = name.Trim();
        if (name.Length == 0 || Find(c, name) is not null) return null;
        var source = c.Active ?? new DesktopLayout { Name = fallbackName };
        var layouts = c.Layouts.Count == 0 ? [source] : c.Layouts.ToList();
        layouts.Add(source with { Name = name, Hotkey = "" });
        return c with { ActiveLayout = name, Layouts = layouts };
    }

    public static DesktopConfig? Rename(DesktopConfig c, string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || Find(c, oldName) is not { } layout) return null;
        if (Find(c, newName) is { } other && other != layout) return null;
        return c with
        {
            ActiveLayout = c.ActiveLayout == layout.Name ? newName : c.ActiveLayout,
            Layouts = c.Layouts.Select(l => l == layout ? l with { Name = newName } : l).ToList(),
        };
    }

    /// <summary>The last layout cannot be deleted. Deleting the active one activates the first left.</summary>
    public static DesktopConfig? Delete(DesktopConfig c, string name)
    {
        if (c.Layouts.Count < 2 || Find(c, name) is not { } layout) return null;
        var left = c.Layouts.Where(l => l != layout).ToList();
        return c with { ActiveLayout = c.ActiveLayout == layout.Name ? left[0].Name : c.ActiveLayout, Layouts = left };
    }

    /// <summary>Empty clears it. A shortcut belongs to one layout only, so others lose it.</summary>
    public static DesktopConfig? SetHotkey(DesktopConfig c, string name, string hotkey)
    {
        hotkey = hotkey.Trim();
        if (Find(c, name) is not { } layout) return null;
        if (hotkey.Length > 0 && !Hotkey.TryParse(hotkey, out _, out _)) return null;
        return c with
        {
            Layouts = c.Layouts.Select(l =>
                l == layout ? l with { Hotkey = hotkey } :
                hotkey.Length > 0 && string.Equals(l.Hotkey, hotkey, StringComparison.OrdinalIgnoreCase) ? l with { Hotkey = "" } :
                l).ToList(),
        };
    }

    private static DesktopLayout? Find(DesktopConfig c, string name) =>
        c.Layouts.FirstOrDefault(l => string.Equals(l.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}
