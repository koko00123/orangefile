using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orange.Core;

public enum ModKey { Shift, Ctrl, Alt }

public enum OutputMode { NextToOriginal, Folder }

/// <summary>How desktop boxes are drawn: behind the icons (wallpaper layer) or as a frame with a hole.</summary>
public enum DesktopRenderMode { Wallpaper, Frame }

/// <summary>What happens to boxes whose monitor is not connected.</summary>
public enum DesktopOrphanBoxes { Hide, MoveToPrimary }

/// <summary>User preferences, stored as JSON in %LOCALAPPDATA%\Orange\settings.json.</summary>
public sealed class Settings
{
    // First run: follow the Windows display language when Orange speaks it.
    public string Language { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "es" => "es",
        "pt" => "pt",
        _ => "en",
    };
    public double WheelScale { get; set; } = 0.8;
    public ModKey ConvertKey { get; set; } = ModKey.Shift;
    public ModKey ToolsKey { get; set; } = ModKey.Ctrl;
    public ModKey ActionsKey { get; set; } = ModKey.Alt;
    public OutputMode Output { get; set; } = OutputMode.NextToOriginal;
    public string OutputFolder { get; set; } = "";
    public int ImageQuality { get; set; } = 90;
    public bool Notifications { get; set; } = true;

    /// <summary>Drags only open the wheel when they start in one of these processes (browsers etc. are left alone).</summary>
    public List<string> DragSourceApps { get; set; } = ["explorer.exe"];

    // Updates
    public bool AutoCheckUpdates { get; set; } = true;

    // Desktop boxes (off by default; boxes themselves live in desktop.json)
    public bool DesktopModuleEnabled { get; set; } = false;
    public DesktopRenderMode DesktopRenderMode { get; set; } = DesktopRenderMode.Wallpaper;
    public DesktopOrphanBoxes DesktopOrphanBoxes { get; set; } = DesktopOrphanBoxes.Hide;
    public string DesktopEditHotkey { get; set; } = "Ctrl+Alt+D";
    /// <summary>Hides/shows every desktop icon and box. Empty = no shortcut (tray menu only).</summary>
    public string DesktopHideHotkey { get; set; } = "";

    // Phone sharing ("AirDrop" over Wi-Fi)
    public bool PhoneSharing { get; set; } = true;
    public int PhonePort { get; set; } = 8765;
    public string PhoneToken { get; set; } = "";
    public string PhoneInbox { get; set; } = "";

    /// <summary>Where files from the phone land (default: Downloads\From iPhone).</summary>
    public string PhoneInboxOrDefault => Directory.Exists(PhoneInbox) ? PhoneInbox
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "From iPhone");

    private static readonly string FilePath = Path.Combine(Paths.AppData, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Current { get; private set; } = Load();

    /// <summary>Raised after <see cref="Save"/> so open UI can refresh (e.g. a new language).</summary>
    public static event Action? Changed;

    public WheelMode? ModeFor(ModKey key) =>
        key == ConvertKey ? WheelMode.Convert :
        key == ToolsKey ? WheelMode.Tools :
        key == ActionsKey ? WheelMode.Actions : null;

    public ModKey KeyFor(WheelMode mode) => mode switch
    {
        WheelMode.Convert => ConvertKey,
        WheelMode.Tools => ToolsKey,
        _ => ActionsKey,
    };

    public static string KeyName(ModKey key) => key == ModKey.Ctrl ? "Ctrl" : key.ToString();

    private static Settings Load()
    {
        Settings? s = null;
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json);
        }
        catch (Exception ex)
        {
            JobRunner.Log($"Could not read settings, using defaults: {ex.Message}");
        }

        s ??= new Settings();
        bool newToken = s.PhoneToken.Length < 16;
        s = Sanitize(s);
        // Keep a freshly generated phone key, or the QR code would change on every start.
        if (newToken)
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(s, Json)); } catch { }
        }
        return s;
    }

    public static void Save(Settings updated)
    {
        Current = Sanitize(updated);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json));
        Changed?.Invoke();
    }

    public Settings Clone() => (Settings)MemberwiseClone();

    public static string NewToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');

    private static Settings Sanitize(Settings s)
    {
        s.WheelScale = Math.Clamp(s.WheelScale, 0.5, 1.2);
        s.ImageQuality = Math.Clamp(s.ImageQuality, 40, 100);
        if (s.PhonePort is < 1024 or > 65535) s.PhonePort = 8765;
        // The secret key in the QR code. Generated once; "New key" in the pairing window replaces it.
        if (s.PhoneToken.Length < 16) s.PhoneToken = NewToken();
        if (!Loc.Languages.ContainsKey(s.Language)) s.Language = "en";
        if (s.DragSourceApps is not { Count: > 0 }) s.DragSourceApps = ["explorer.exe"];
        if (string.IsNullOrWhiteSpace(s.DesktopEditHotkey)) s.DesktopEditHotkey = "Ctrl+Alt+D";
        s.DesktopHideHotkey ??= "";
        // Every mode needs its own key; fall back to the defaults if two collide.
        if (s.ConvertKey == s.ToolsKey || s.ToolsKey == s.ActionsKey || s.ConvertKey == s.ActionsKey)
            (s.ConvertKey, s.ToolsKey, s.ActionsKey) = (ModKey.Shift, ModKey.Ctrl, ModKey.Alt);
        return s;
    }
}
