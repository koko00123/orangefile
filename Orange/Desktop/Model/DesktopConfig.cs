using System.Text.Json.Serialization;

namespace Orange.Desktop.Model;

/// <summary>Everything the Desktop module stores (desktop.json). Immutable: edits produce a new instance.</summary>
public sealed record DesktopConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public string ActiveLayout { get; init; } = "";
    public IReadOnlyList<DesktopLayout> Layouts { get; init; } = [];

    public static DesktopConfig Empty { get; } = new();

    public DesktopLayout? Active => Layouts.FirstOrDefault(l => l.Name == ActiveLayout);
}

/// <summary>A named arrangement of boxes (e.g. "Home", "Work").</summary>
public sealed record DesktopLayout
{
    public string Name { get; init; } = "";
    public IReadOnlyList<Box> Boxes { get; init; } = [];

    /// <summary>Optional global shortcut that switches to this layout ("" = none).</summary>
    public string Hotkey { get; init; } = "";

    /// <summary>Items the user pulled out of a box on purpose, so rules must not put them back.</summary>
    public IReadOnlyList<string> LooseItems { get; init; } = [];
}

/// <summary>A box of desktop icons. It only owns icon positions, never the files.</summary>
public sealed record Box
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Color { get; init; } = "#2B2B2B";
    public string TitleColor { get; init; } = "#FFFFFF";
    public MonitorKey Monitor { get; init; } = MonitorKey.Unknown;

    /// <summary>Position as fractions of the monitor's work area, so it survives resolution and scale changes.</summary>
    public RelRect Rect { get; init; } = new(0.05, 0.05, 0.2, 0.3);

    public BoxSort Sort { get; init; } = BoxSort.Name;

    /// <summary>Item names (parent-relative parsing names, e.g. "Report.pdf"). Order is the manual order.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    public IReadOnlyList<BoxRule> Rules { get; init; } = [];

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}

/// <summary>Stable monitor identity: device path when available, plus a readable name for the UI.</summary>
public sealed record MonitorKey(string Id, string Name)
{
    public static MonitorKey Unknown { get; } = new("", "");
}

public sealed record RelRect(double X, double Y, double W, double H);

[JsonConverter(typeof(JsonStringEnumConverter<BoxSort>))]
public enum BoxSort { Name, Type, Manual }

[JsonConverter(typeof(JsonStringEnumConverter<ItemKind>))]
public enum ItemKind { Shortcut, Folder, File }

/// <summary>Puts items that have no box into this box. Evaluated in box order, then rule order; first match wins.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExtensionRule), "Extension")]
[JsonDerivedType(typeof(NameContainsRule), "NameContains")]
[JsonDerivedType(typeof(KindRule), "Kind")]
[JsonDerivedType(typeof(RegexRule), "Regex")]
public abstract record BoxRule;

/// <summary>Extensions separated by ';', with or without the dot: ".pdf;docx".</summary>
public sealed record ExtensionRule(string Value) : BoxRule;

/// <summary>Case-insensitive text inside the item name.</summary>
public sealed record NameContainsRule(string Value) : BoxRule;

public sealed record KindRule(ItemKind Value) : BoxRule;

/// <summary>.NET regex over the item name, run with a timeout.</summary>
public sealed record RegexRule(string Value) : BoxRule;
