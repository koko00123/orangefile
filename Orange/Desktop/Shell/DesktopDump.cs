using System.Text;
using Orange.Core;
using Orange.Desktop.Layout;

namespace Orange.Desktop.Shell;

/// <summary>
/// Diagnostic command: <c>Orange.exe --desktop-dump report.md [--probe "Item name"]</c>.
/// Writes monitors, the desktop view and every icon to a Markdown file. With --probe it moves one icon
/// to the middle of each monitor, reads its position back and puts it where it was (checks P2/P3).
/// The report contains the user's file names: it is for the user, never for the repo.
/// </summary>
internal static class DesktopDump
{
    public static int Run(string outPath, string? probe)
    {
        var md = new StringBuilder();
        int code = 0;
        md.AppendLine("# Orange — desktop dump").AppendLine()
          .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · Orange {typeof(DesktopDump).Assembly.GetName().Version} · Windows {Environment.OSVersion.Version}")
          .AppendLine().AppendLine("> Contains your file names. Do not commit it to the repository.").AppendLine();

        MonitorMap? map = null;
        try
        {
            map = new MonitorMap(new Win32Monitors().GetMonitors());
            WriteMonitors(md, map);
        }
        catch (Exception ex) { md.AppendLine($"**Monitors failed:** `{ex}`").AppendLine(); code = 1; }

        try
        {
            using var icons = new ShellDesktopIcons();
            var info = icons.GetViewInfo();
            md.AppendLine("## Desktop view").AppendLine()
              .AppendLine($"- List view client origin (screen): {info.ListOrigin.X}, {info.ListOrigin.Y}")
              .AppendLine($"- Virtual screen: {Input.Native.GetSystemMetrics(76)}, {Input.Native.GetSystemMetrics(77)}, {Input.Native.GetSystemMetrics(78)} × {Input.Native.GetSystemMetrics(79)}")
              .AppendLine($"- Spacing: {info.Spacing.W} × {info.Spacing.H} (default {info.DefaultSpacing.W} × {info.DefaultSpacing.H})")
              .AppendLine($"- Auto arrange: {info.AutoArrange} · Align to grid: {info.SnapToGrid}")
              .AppendLine();

            var items = icons.GetItems();
            WriteItems(md, items, map, info.Spacing);

            if (!string.IsNullOrEmpty(probe))
            {
                try { code |= Probe(md, icons, items, map, probe, info.Spacing); }
                finally { icons.RestoreArrangement(info); }
            }
        }
        catch (Exception ex) { md.AppendLine($"**Desktop view failed:** `{ex}`").AppendLine(); code = 1; }

        try { File.WriteAllText(outPath, md.ToString()); }
        catch (Exception ex) { JobRunner.Log($"Desktop dump: could not write {outPath}: {ex.Message}"); return 2; }
        return code;
    }

    private static void WriteMonitors(StringBuilder md, MonitorMap map)
    {
        md.AppendLine($"## Monitors ({map.Monitors.Count})").AppendLine()
          .AppendLine("| # | Name | Primary | Bounds (x, y, w × h) | Work area | DPI (scale) | Key |")
          .AppendLine("|---|---|---|---|---|---|---|");
        for (int i = 0; i < map.Monitors.Count; i++)
        {
            var m = map.Monitors[i];
            md.AppendLine($"| {i + 1} | {m.Key.Name} | {(m.IsPrimary ? "yes" : "")} | {R(m.Bounds)} | {R(m.WorkArea)} | {m.Dpi} ({m.Dpi * 100 / 96} %) | `{m.Key.Id}` |");
        }
        md.AppendLine();
    }

    private static void WriteItems(StringBuilder md, IReadOnlyList<DesktopItem> items, MonitorMap? map, PixelSize cell)
    {
        md.AppendLine($"## Items ({items.Count})").AppendLine()
          .AppendLine("| Name | Kind | Position | Monitor |")
          .AppendLine("|---|---|---|---|");
        foreach (var it in items.OrderBy(i => i.Position.X).ThenBy(i => i.Position.Y))
            md.AppendLine($"| {Escape(it.Name)} | {it.Kind} | {it.Position.X}, {it.Position.Y} | {MonitorNumber(map, it.Position, cell)} |");
        md.AppendLine();
    }

    private static int Probe(StringBuilder md, IDesktopIcons icons, IReadOnlyList<DesktopItem> items, MonitorMap? map,
        string probe, PixelSize cell)
    {
        md.AppendLine($"## Probe: `{Escape(probe)}`").AppendLine();
        var item = items.FirstOrDefault(i => string.Equals(i.Name, probe, StringComparison.OrdinalIgnoreCase));
        if (item is null || map is null)
        {
            md.AppendLine("Item not found (use the exact name from the table above, with extension).").AppendLine();
            return 1;
        }

        md.AppendLine($"- Free positioning changed: {icons.EnsureFreePositioning()}").AppendLine()
          .AppendLine("| Monitor | Target | Read back | Δ | On monitor |")
          .AppendLine("|---|---|---|---|---|");
        int code = 0;
        try
        {
            for (int i = 0; i < map.Monitors.Count; i++)
            {
                var c = map.Monitors[i].WorkArea.Center;
                var target = new PixelPoint(c.X - cell.W / 2, c.Y - cell.H / 2);
                icons.SetPositions([(item.Name, target)]);
                Thread.Sleep(400);   // let Explorer apply it before reading back
                var back = icons.GetItems().FirstOrDefault(x => x.Name == item.Name)?.Position;
                var delta = back is { } b ? $"{b.X - target.X}, {b.Y - target.Y}" : "?";
                var on = back is { } bb ? MonitorNumber(map, bb, cell) : "?";
                if (back != target) code = 1;
                md.AppendLine($"| {i + 1} | {target.X}, {target.Y} | {(back is { } p ? $"{p.X}, {p.Y}" : "missing")} | {delta} | {on} |");
            }
        }
        finally
        {
            icons.SetPositions([(item.Name, item.Position)]);
            md.AppendLine().AppendLine($"Restored to {item.Position.X}, {item.Position.Y}.").AppendLine();
        }
        return code;
    }

    private static string MonitorNumber(MonitorMap? map, PixelPoint p, PixelSize cell)
    {
        if (map is null) return "?";
        var center = new PixelPoint(p.X + cell.W / 2, p.Y + cell.H / 2);
        var m = map.Monitors.FirstOrDefault(x => x.Bounds.Contains(center));
        return m is null ? "none" : (map.Monitors.ToList().IndexOf(m) + 1).ToString();
    }

    private static string R(PixelRect r) => $"{r.X}, {r.Y}, {r.W} × {r.H}";
    private static string Escape(string s) => s.Replace("|", "\\|");
}
