using Orange.Desktop.Model;

namespace Orange.Desktop.Layout;

/// <summary>Which box each icon belongs to, from where the icons are on screen.</summary>
public static class Membership
{
    /// <summary>
    /// Box id for each item (null = outside every box), using the icon's center.
    /// Boxes earlier in the list win where boxes overlap.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> FromPositions(
        IReadOnlyList<(string BoxId, PixelRect Rect)> boxes, IReadOnlyList<DesktopItem> items, PixelSize cell)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var center = new PixelPoint(item.Position.X + cell.W / 2, item.Position.Y + cell.H / 2);
            result[item.Name] = boxes.FirstOrDefault(b => b.Rect.Contains(center)).BoxId;
        }
        return result;
    }

    /// <summary>
    /// Applies what the user did with the mouse: items dropped into a box join it (at the end),
    /// items dragged out of a box become loose on purpose, so rules leave them alone.
    /// Items not in <paramref name="observed"/> are left as they were.
    /// </summary>
    public static DesktopLayout Reassign(DesktopLayout layout, IReadOnlyDictionary<string, string?> observed)
    {
        var current = OwnerOf(layout);
        var moves = observed
            .Where(kv => !string.Equals(current.GetValueOrDefault(kv.Key), kv.Value, StringComparison.Ordinal))
            .ToList();
        if (moves.Count == 0) return layout;

        var moved = moves.Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var boxes = layout.Boxes.Select(b => b with
        {
            Items = b.Items.Where(i => !moved.Contains(i))
                .Concat(moves.Where(m => m.Value == b.Id).Select(m => m.Key))
                .ToList(),
        }).ToList();

        var leftBoxes = moves.Where(m => m.Value is null && current.GetValueOrDefault(m.Key) is not null).Select(m => m.Key);
        var loose = layout.LooseItems
            .Where(i => !moves.Any(m => m.Value is not null && string.Equals(m.Key, i, StringComparison.OrdinalIgnoreCase)))
            .Concat(leftBoxes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return layout with { Boxes = boxes, LooseItems = loose };
    }

    /// <summary>Item name → id of the box that lists it.</summary>
    public static Dictionary<string, string> OwnerOf(DesktopLayout layout)
    {
        var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in layout.Boxes)
            foreach (var i in b.Items)
                owner.TryAdd(i, b.Id);
        return owner;
    }
}
