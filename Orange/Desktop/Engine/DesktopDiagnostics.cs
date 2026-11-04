using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Orange.Core;
using Orange.Desktop.Layout;
using Orange.Desktop.Shell;
using Orange.Desktop.Store;

namespace Orange.Desktop.Engine;

/// <summary>
/// Command-line helpers for testing the module on a real desktop without touching the running Orange:
/// <code>
/// --desktop-backup  positions.json             save every icon position
/// --desktop-restore positions.json             put icons back from a backup
/// --desktop-preview desktop.json seconds report.txt [--mode Wallpaper|Frame]
/// </code>
/// </summary>
internal static class DesktopDiagnostics
{
    private sealed record SavedPosition(string Name, int X, int Y);
    private sealed record Saved(bool AutoArrange, bool SnapToGrid, List<SavedPosition> Items);

    public static int Backup(string path)
    {
        try
        {
            using var icons = new ShellDesktopIcons();
            var info = icons.GetViewInfo();
            var saved = new Saved(info.AutoArrange, info.SnapToGrid,
                icons.GetItems().Select(i => new SavedPosition(i.Name, i.Position.X, i.Position.Y)).ToList());
            File.WriteAllText(path, JsonSerializer.Serialize(saved, DesktopStore.Json));
            return 0;
        }
        catch (Exception ex) { JobRunner.Log($"Desktop backup failed: {ex}"); return 1; }
    }

    public static int Restore(string path)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), DesktopStore.Json)
                ?? throw new InvalidDataException("Empty backup");
            using var icons = new ShellDesktopIcons();
            // Positions first with free positioning, then the grid options, so nothing snaps on the way.
            icons.EnsureFreePositioning();
            icons.SetPositions(saved.Items.Select(s => (s.Name, new PixelPoint(s.X, s.Y))).ToList());
            var now = icons.GetViewInfo();
            icons.RestoreArrangement(now with { AutoArrange = saved.AutoArrange, SnapToGrid = saved.SnapToGrid });
            return 0;
        }
        catch (Exception ex) { JobRunner.Log($"Desktop restore failed: {ex}"); return 1; }
    }

    /// <summary>Shows a config's boxes for some seconds, writes RAM/mode figures, then closes the boxes.</summary>
    public static void Preview(string config, int seconds, string report, string? mode, Action<int> exit)
    {
        if (Enum.TryParse<DesktopRenderMode>(mode, true, out var m)) Settings.Current.DesktopRenderMode = m;   // not saved

        var proc = Process.GetCurrentProcess();
        (long Private, long WorkingSet) Measure()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            proc.Refresh();
            return (proc.PrivateMemorySize64, proc.WorkingSet64);
        }

        var before = Measure();
        var sw = Stopwatch.StartNew();
        var engine = new DesktopEngine(new ShellDesktopIcons(), new Win32Monitors(), new DesktopStore(config));
        engine.Start();
        var applyMs = sw.ElapsedMilliseconds;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, seconds)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var after = Measure();
            var sb = new StringBuilder()
                .AppendLine($"mode: {engine.ActiveMode} (asked {Settings.Current.DesktopRenderMode})")
                .AppendLine($"boxes drawn: {engine.BoxCount}")
                .AppendLine($"icons moved: {engine.IconsMoved}")
                .AppendLine($"apply time: {applyMs} ms")
                .AppendLine($"private bytes: {before.Private / 1048576.0:F1} MB → {after.Private / 1048576.0:F1} MB (+{(after.Private - before.Private) / 1048576.0:F1} MB)")
                .AppendLine($"working set:   {before.WorkingSet / 1048576.0:F1} MB → {after.WorkingSet / 1048576.0:F1} MB (+{(after.WorkingSet - before.WorkingSet) / 1048576.0:F1} MB)");
            try { File.WriteAllText(report, sb.ToString()); } catch { }
            int code = engine.BoxCount > 0 ? 0 : 1;   // read before Dispose closes the boxes
            engine.Dispose();
            exit(code);
        };
        timer.Start();
    }
}
