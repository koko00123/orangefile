using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Orange.Desktop.Model;

namespace Orange.Desktop.Layout;

/// <summary>Puts items that have no box into the first box whose rules match (SPEC §7).</summary>
public static class RuleMatcher
{
    /// <summary>A hand-written regex must never freeze the desktop.</summary>
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    // Compiled once per pattern; null marks an invalid pattern so it is not parsed again.
    private static readonly ConcurrentDictionary<string, Regex?> Cache = new();

    /// <summary>Box id for an item, or null when it already has a box, is loose on purpose, or nothing matches.</summary>
    public static string? Match(DesktopLayout layout, DesktopItem item)
    {
        if (layout.LooseItems.Contains(item.Name, StringComparer.OrdinalIgnoreCase)) return null;
        if (layout.Boxes.Any(b => b.Items.Contains(item.Name, StringComparer.OrdinalIgnoreCase))) return null;
        return layout.Boxes.FirstOrDefault(b => b.Rules.Any(r => Matches(r, item)))?.Id;
    }

    /// <summary>Adds every matching new item to the end of its box.</summary>
    public static DesktopLayout AssignNew(DesktopLayout layout, IReadOnlyList<DesktopItem> items)
    {
        var additions = items
            .Select(i => (i.Name, BoxId: Match(layout, i)))
            .Where(a => a.BoxId is not null)
            .ToList();
        if (additions.Count == 0) return layout;

        return layout with
        {
            Boxes = layout.Boxes.Select(b => b with
            {
                Items = b.Items.Concat(additions.Where(a => a.BoxId == b.Id).Select(a => a.Name)).ToList(),
            }).ToList(),
        };
    }

    public static bool Matches(BoxRule rule, DesktopItem item) => rule switch
    {
        ExtensionRule r => MatchesExtension(r.Value, item.Name),
        NameContainsRule r => !string.IsNullOrEmpty(r.Value) && item.Name.Contains(r.Value, StringComparison.OrdinalIgnoreCase),
        KindRule r => r.Value == item.Kind,
        RegexRule r => MatchesRegex(r.Value, item.Name),
        _ => false,
    };

    private static bool MatchesExtension(string list, string name)
    {
        var ext = Path.GetExtension(name);
        if (ext.Length == 0) return false;
        foreach (var part in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var wanted = part.StartsWith('.') ? part : "." + part;
            if (string.Equals(wanted, ext, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool MatchesRegex(string pattern, string name)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        var regex = Cache.GetOrAdd(pattern, static p =>
        {
            try { return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout); }
            catch (ArgumentException) { return null; }
        });
        if (regex is null) return false;
        try { return regex.IsMatch(name); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
