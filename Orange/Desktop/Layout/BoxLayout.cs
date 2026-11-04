using Orange.Desktop.Model;

namespace Orange.Desktop.Layout;

/// <summary>Result of laying out one box: where each icon goes, and the icons that did not fit.</summary>
public sealed record BoxPlacement(IReadOnlyList<(string Name, PixelPoint Position)> Placed, IReadOnlyList<string> Overflow);

/// <summary>Grid layout of icons inside a box. Pure math, no shell calls.</summary>
public static class BoxLayout
{
    /// <summary>
    /// Lays <paramref name="items"/> out in rows below the title bar of <paramref name="box"/>.
    /// <paramref name="cell"/> is the desktop icon spacing (one icon plus its label).
    /// </summary>
    public static BoxPlacement Place(PixelRect box, PixelSize cell, int titleHeight, int padding,
        IReadOnlyList<DesktopItem> items, BoxSort sort, IReadOnlyList<string> manualOrder)
    {
        var ordered = Order(items, sort, manualOrder);
        var innerW = box.W - 2 * padding;
        var innerH = box.H - titleHeight - 2 * padding;
        int cols = cell.W > 0 && innerW >= cell.W ? innerW / cell.W : 0;
        int rows = cell.H > 0 && innerH >= cell.H ? innerH / cell.H : 0;
        int capacity = cols * rows;

        // Center the grid horizontally so the leftover space is split on both sides.
        int left = box.X + padding + (innerW - cols * cell.W) / 2;
        int top = box.Y + titleHeight + padding;

        var placed = new List<(string, PixelPoint)>();
        var overflow = new List<string>();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i < capacity)
                placed.Add((ordered[i].Name, new PixelPoint(left + i % cols * cell.W, top + i / cols * cell.H)));
            else
                overflow.Add(ordered[i].Name);
        }
        return new BoxPlacement(placed, overflow);
    }

    public static IReadOnlyList<DesktopItem> Order(IReadOnlyList<DesktopItem> items, BoxSort sort, IReadOnlyList<string> manualOrder)
    {
        var byName = NaturalComparer.Instance;
        return sort switch
        {
            // Folders first, then by extension, then by name, as Explorer's "Sort by type" roughly does.
            BoxSort.Type => items
                .OrderBy(i => i.Kind == ItemKind.Folder ? 0 : 1)
                .ThenBy(i => Path.GetExtension(i.Name), StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, byName)
                .ToList(),
            // Manual: the stored order; items the box does not list yet go last, by name.
            BoxSort.Manual => items
                .OrderBy(i => IndexIn(manualOrder, i.Name))
                .ThenBy(i => i.Name, byName)
                .ToList(),
            _ => items.OrderBy(i => i.Name, byName).ToList(),
        };
    }

    private static int IndexIn(IReadOnlyList<string> order, string name)
    {
        for (int i = 0; i < order.Count; i++)
            if (string.Equals(order[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }
}

/// <summary>"File 2" before "File 10", case-insensitive, like Explorer.</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static NaturalComparer Instance { get; } = new();

    public int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var na = a.AsSpan(si, i - si).TrimStart('0');
                var nb = b.AsSpan(sj, j - sj).TrimStart('0');
                // Longer number (without leading zeros) is bigger; same length compares digit by digit.
                int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : na.SequenceCompareTo(nb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        int rest = (a.Length - i).CompareTo(b.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(a, b);
    }
}
