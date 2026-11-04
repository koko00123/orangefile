using System.Text.Json;
using System.Text.Json.Serialization;
using Orange.Core;
using Orange.Desktop.Model;

namespace Orange.Desktop.Store;

/// <summary>Loads and saves desktop.json. Never throws: a broken file is set aside and the module starts empty.</summary>
public sealed class DesktopStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _path;
    private readonly Action<string> _log;

    public DesktopStore(string path, Action<string>? log = null)
    {
        _path = path;
        _log = log ?? JobRunner.Log;
    }

    public static DesktopStore Default() => new(System.IO.Path.Combine(Paths.AppData, "desktop.json"));

    public string FilePath => _path;

    public DesktopConfig Load()
    {
        if (!File.Exists(_path)) return DesktopConfig.Empty;
        try
        {
            var config = JsonSerializer.Deserialize<DesktopConfig>(File.ReadAllText(_path), Json)
                ?? throw new JsonException("File is empty.");
            return Sanitize(config);
        }
        catch (Exception ex)
        {
            _log($"Desktop: could not read {System.IO.Path.GetFileName(_path)}, starting empty: {ex.Message}");
            SetAsideBrokenFile();
            return DesktopConfig.Empty;
        }
    }

    /// <summary>Writes to a temp file and renames it, so a crash mid-write never leaves a half file.</summary>
    public bool Save(DesktopConfig config)
    {
        var tmp = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllText(tmp, JsonSerializer.Serialize(Sanitize(config), Json));
            File.Move(tmp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _log($"Desktop: could not save settings: {ex.Message}");
            try { File.Delete(tmp); } catch { }
            return false;
        }
    }

    private void SetAsideBrokenFile()
    {
        // Keep the user's file for inspection instead of overwriting it on the next save.
        try { File.Move(_path, _path + ".bad", overwrite: true); }
        catch (Exception ex) { _log($"Desktop: could not move the broken file aside: {ex.Message}"); }
    }

    /// <summary>Repairs values a hand-edited or older file may carry (nulls, out-of-range rects, duplicates).</summary>
    public static DesktopConfig Sanitize(DesktopConfig c)
    {
        var layouts = (c.Layouts ?? [])
            .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.Name))
            .DistinctBy(l => l.Name)
            .Select(SanitizeLayout)
            .ToList();

        var active = layouts.Any(l => l.Name == c.ActiveLayout) ? c.ActiveLayout : layouts.FirstOrDefault()?.Name ?? "";
        return c with { Version = DesktopConfig.CurrentVersion, ActiveLayout = active, Layouts = layouts };
    }

    private static DesktopLayout SanitizeLayout(DesktopLayout l)
    {
        // An item belongs to one box only: the first box that lists it keeps it.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>();
        var boxes = new List<Box>();
        foreach (var b in l.Boxes ?? [])
        {
            if (b is null) continue;
            var id = string.IsNullOrEmpty(b.Id) || !ids.Add(b.Id) ? NewUniqueId(ids) : b.Id;
            boxes.Add(b with
            {
                Id = id,
                Title = b.Title ?? "",
                Color = b.Color ?? "#2B2B2B",
                TitleColor = b.TitleColor ?? "#FFFFFF",
                Monitor = b.Monitor is null ? MonitorKey.Unknown : new MonitorKey(b.Monitor.Id ?? "", b.Monitor.Name ?? ""),
                Rect = Clamp(b.Rect),
                Items = (b.Items ?? []).Where(i => !string.IsNullOrEmpty(i) && taken.Add(i)).ToList(),
                Rules = (b.Rules ?? []).Where(r => r is not null).ToList(),
            });
        }

        var loose = (l.LooseItems ?? []).Where(i => !string.IsNullOrEmpty(i) && !taken.Contains(i))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return l with { Boxes = boxes, LooseItems = loose, Hotkey = l.Hotkey ?? "" };
    }

    private static string NewUniqueId(HashSet<string> ids)
    {
        string id;
        do id = Box.NewId(); while (!ids.Add(id));
        return id;
    }

    private const double MinSize = 0.02;

    private static RelRect Clamp(RelRect? r)
    {
        if (r is null || !double.IsFinite(r.X + r.Y + r.W + r.H)) return new Box().Rect;
        var w = Math.Clamp(r.W, MinSize, 1);
        var h = Math.Clamp(r.H, MinSize, 1);
        return new RelRect(Math.Clamp(r.X, 0, 1 - w), Math.Clamp(r.Y, 0, 1 - h), w, h);
    }
}
