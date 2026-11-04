using System.Diagnostics;
using Orange.Core;
using Orange.Desktop.Layout;
using Orange.Desktop.Model;

namespace Orange.Tests.Desktop;

/// <summary>Three synthetic monitors: one left of the primary (negative X), and one taller and higher (negative Y).</summary>
internal static class ThreeMonitors
{
    public static readonly MonitorKey A = new("mon-a", "Left 1080p");
    public static readonly MonitorKey B = new("mon-b", "Middle 1440p");
    public static readonly MonitorKey C = new("mon-c", "Right 4K");

    // 100 %, 125 % and 150 % scale; taskbar only cuts the bottom of each work area.
    public static readonly MonitorInfo Left = new(A, new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040), 96, false);
    public static readonly MonitorInfo Middle = new(B, new(0, 0, 2560, 1440), new(0, 0, 2560, 1392), 120, true);
    public static readonly MonitorInfo Right = new(C, new(2560, -200, 3840, 2160), new(2560, -200, 3840, 2100), 144, false);

    // Deliberately unordered: the map must sort them left to right.
    public static MonitorMap Map(DesktopOrphanBoxes orphans = DesktopOrphanBoxes.Hide) => new([Right, Left, Middle], orphans);
}

public class MonitorMapTests
{
    [Fact]
    public void MonitorsAreNumberedLeftToRight()
    {
        var map = ThreeMonitors.Map();
        Assert.Equal([ThreeMonitors.A, ThreeMonitors.B, ThreeMonitors.C], map.Monitors.Select(m => m.Key));
        Assert.Equal(1, map.NumberOf(ThreeMonitors.A));
        Assert.Equal(3, map.NumberOf(ThreeMonitors.C));
        Assert.Equal(0, map.NumberOf(new MonitorKey("unplugged", "")));
        Assert.Equal(ThreeMonitors.B, map.Primary!.Key);
    }

    [Fact]
    public void FractionsBecomePixelsOnTheBoxMonitor()
    {
        var map = ThreeMonitors.Map();
        Assert.Equal(new PixelRect(-960, 0, 480, 520),
            map.ToPixels(new Box { Monitor = ThreeMonitors.A, Rect = new(0.5, 0, 0.25, 0.5) }));
        Assert.Equal(new PixelRect(2560, -200, 1920, 1050),
            map.ToPixels(new Box { Monitor = ThreeMonitors.C, Rect = new(0, 0, 0.5, 0.5) }));
    }

    [Theory]
    [InlineData("mon-a")]
    [InlineData("mon-b")]
    [InlineData("mon-c")]
    public void PixelsRoundTripToTheSameMonitorAndFractions(string id)
    {
        var map = ThreeMonitors.Map();
        var key = map.Monitors.Single(m => m.Key.Id == id).Key;
        var rect = new RelRect(0.1, 0.2, 0.3, 0.4);

        var back = map.ToRelative(map.ToPixels(new Box { Monitor = key, Rect = rect })!.Value)!.Value;

        Assert.Equal(key, back.Key);
        Assert.Equal(rect.X, back.Rect.X, 3);
        Assert.Equal(rect.Y, back.Rect.Y, 3);
        Assert.Equal(rect.W, back.Rect.W, 3);
        Assert.Equal(rect.H, back.Rect.H, 3);
    }

    [Fact]
    public void BoxOfMissingMonitorIsHiddenOrMovedToPrimary()
    {
        var box = new Box { Monitor = new MonitorKey("unplugged", "Old TV"), Rect = new(0, 0, 0.5, 0.5) };

        Assert.Null(ThreeMonitors.Map(DesktopOrphanBoxes.Hide).ToPixels(box));
        Assert.Equal(new PixelRect(0, 0, 1280, 696), ThreeMonitors.Map(DesktopOrphanBoxes.MoveToPrimary).ToPixels(box));
    }

    [Fact]
    public void DraggedBoxBelongsToTheMonitorHoldingItsCenterAndIsClamped()
    {
        var map = ThreeMonitors.Map();

        // Mostly on the right monitor, sticking out above its top edge.
        var (key, rect) = map.ToRelative(new PixelRect(2400, -400, 960, 1050))!.Value;

        Assert.Equal(ThreeMonitors.C, key);
        Assert.Equal(0, rect.X, 3);   // pulled inside the work area
        Assert.Equal(0, rect.Y, 3);
        Assert.Equal(0.25, rect.W, 3);
        Assert.Equal(0.5, rect.H, 3);
    }

    [Fact]
    public void PointOutsideEveryMonitorUsesTheNearest()
    {
        // Below the left monitor (it is shorter than the others).
        Assert.Equal(ThreeMonitors.A, ThreeMonitors.Map().MonitorAt(new PixelPoint(-1000, 1300))!.Key);
    }
}

public class BoxLayoutTests
{
    private static DesktopItem Item(string name, ItemKind kind = ItemKind.File) => new(name, kind, default);

    [Fact]
    public void FillsRowsBelowTheTitleAndReportsOverflow()
    {
        var items = Enumerable.Range(1, 8).Select(i => Item($"f{i}.txt")).ToList();

        var p = BoxLayout.Place(new PixelRect(-500, 100, 300, 300), new PixelSize(100, 100), titleHeight: 30, padding: 0,
            items, BoxSort.Name, []);

        Assert.Equal(6, p.Placed.Count);   // 3 columns × 2 rows fit in 300 × 270
        Assert.Equal(("f1.txt", new PixelPoint(-500, 130)), p.Placed[0]);
        Assert.Equal(("f3.txt", new PixelPoint(-300, 130)), p.Placed[2]);
        Assert.Equal(("f4.txt", new PixelPoint(-500, 230)), p.Placed[3]);
        Assert.Equal(["f7.txt", "f8.txt"], p.Overflow);
    }

    [Fact]
    public void GridIsCenteredHorizontally()
    {
        var p = BoxLayout.Place(new PixelRect(0, 0, 350, 200), new PixelSize(100, 100), 30, 10, [Item("a")], BoxSort.Name, []);
        // Inner width 330 fits 3 columns (300); the 30 px left over is split: 10 + 15.
        Assert.Equal(new PixelPoint(25, 40), p.Placed[0].Position);
    }

    [Fact]
    public void BoxTooSmallForOneIconOverflowsEverything()
    {
        var p = BoxLayout.Place(new PixelRect(0, 0, 80, 300), new PixelSize(100, 100), 30, 0, [Item("a"), Item("b")], BoxSort.Name, []);
        Assert.Empty(p.Placed);
        Assert.Equal(["a", "b"], p.Overflow);
    }

    [Fact]
    public void NameSortIsNaturalAndCaseInsensitive()
    {
        var order = BoxLayout.Order([Item("File 10.txt"), Item("file 1.txt"), Item("File 2.txt"), Item("apple.txt")], BoxSort.Name, []);
        Assert.Equal(["apple.txt", "file 1.txt", "File 2.txt", "File 10.txt"], order.Select(i => i.Name));
    }

    [Fact]
    public void TypeSortPutsFoldersFirstThenGroupsByExtension()
    {
        var order = BoxLayout.Order(
            [Item("b.pdf"), Item("Projects", ItemKind.Folder), Item("a.txt"), Item("a.pdf"), Item("Editor.lnk", ItemKind.Shortcut)],
            BoxSort.Type, []);
        Assert.Equal(["Projects", "Editor.lnk", "a.pdf", "b.pdf", "a.txt"], order.Select(i => i.Name));
    }

    [Fact]
    public void ManualSortKeepsStoredOrderAndAppendsNewItems()
    {
        var order = BoxLayout.Order([Item("a"), Item("new2"), Item("c"), Item("new1"), Item("B")], BoxSort.Manual, ["c", "b", "a"]);
        Assert.Equal(["c", "B", "a", "new1", "new2"], order.Select(i => i.Name));
    }
}

public class MembershipTests
{
    private static readonly PixelSize Cell = new(100, 100);

    [Fact]
    public void IconCenterDecidesTheBoxAndEarlierBoxesWinOverlaps()
    {
        var boxes = new List<(string, PixelRect)> { ("top", new(0, 0, 300, 300)), ("under", new(200, 0, 300, 300)) };
        var items = new List<DesktopItem>
        {
            new("inTop.txt", ItemKind.File, new(10, 10)),
            new("overlap.txt", ItemKind.File, new(200, 10)),   // center 250,60: inside both
            new("inUnder.txt", ItemKind.File, new(380, 10)),
            new("outside.txt", ItemKind.File, new(-2000, 10)),
        };

        var m = Membership.FromPositions(boxes, items, Cell);

        Assert.Equal("top", m["inTop.txt"]);
        Assert.Equal("top", m["overlap.txt"]);
        Assert.Equal("under", m["inUnder.txt"]);
        Assert.Null(m["outside.txt"]);
    }

    [Fact]
    public void ReassignMovesItemsAndRemembersOnesDraggedOut()
    {
        var layout = new DesktopLayout
        {
            Name = "L",
            LooseItems = ["wasLoose.txt"],
            Boxes =
            [
                new Box { Id = "a", Items = ["stays.txt", "toB.txt", "out.txt"] },
                new Box { Id = "b", Items = ["inB.txt"] },
            ],
        };

        var result = Membership.Reassign(layout, new Dictionary<string, string?>
        {
            ["stays.txt"] = "a",
            ["toB.txt"] = "b",
            ["out.txt"] = null,
            ["wasLoose.txt"] = "a",
            ["neverBoxed.txt"] = null,
        });

        Assert.Equal(["stays.txt", "wasLoose.txt"], result.Boxes[0].Items);
        Assert.Equal(["inB.txt", "toB.txt"], result.Boxes[1].Items);
        Assert.Equal(["out.txt"], result.LooseItems);   // never-boxed items are not "loose on purpose"
    }

    [Fact]
    public void OnlyItemsWhosePositionChangedCountAsMoved()
    {
        var before = new Dictionary<string, PixelPoint>
        {
            ["still.txt"] = new(10, 10),
            ["dragged.txt"] = new(110, 10),
            ["overflow.txt"] = new(-500, 900),   // never fit in its box; must not look like a drag
        };
        var now = new List<DesktopItem>
        {
            new("still.txt", ItemKind.File, new(10, 10)),
            new("dragged.txt", ItemKind.File, new(900, 400)),
            new("overflow.txt", ItemKind.File, new(-500, 900)),
            new("brandNew.txt", ItemKind.File, new(0, 0)),
        };

        Assert.Equal(["dragged.txt"], Membership.Moved(before, now).Select(i => i.Name));
        Assert.Equal(["dragged.txt", "brandNew.txt"], Membership.Moved(before, now, includeNew: true).Select(i => i.Name));
    }

    [Fact]
    public void RenameKeepsBoxAndLooseMarkAndFollowsChains()
    {
        var layout = new DesktopLayout
        {
            Name = "L",
            LooseItems = ["old loose.txt"],
            Boxes = [new Box { Id = "a", Items = ["keep.txt", "draft.docx"] }],
        };

        var result = Membership.Rename(layout, [("draft.docx", "v2.docx"), ("V2.DOCX", "final.docx"), ("old loose.txt", "new loose.txt")]);

        Assert.Equal(["keep.txt", "final.docx"], result.Boxes[0].Items);
        Assert.Equal(["new loose.txt"], result.LooseItems);
        Assert.Same(layout, Membership.Rename(layout, []));
    }

    [Fact]
    public void ReassignWithoutChangesReturnsTheSameLayout()
    {
        var layout = new DesktopLayout { Name = "L", Boxes = [new Box { Id = "a", Items = ["x"] }] };
        Assert.Same(layout, Membership.Reassign(layout, new Dictionary<string, string?> { ["x"] = "a", ["y"] = null }));
    }
}

public class RuleMatcherTests
{
    private static DesktopItem File(string name, ItemKind kind = ItemKind.File) => new(name, kind, default);

    private static readonly DesktopLayout Layout = new()
    {
        Name = "L",
        LooseItems = ["loose.pdf"],
        Boxes =
        [
            new Box { Id = "docs", Items = ["already.pdf"], Rules = [new ExtensionRule(".pdf; docx")] },
            new Box { Id = "reports", Rules = [new NameContainsRule("REPORT")] },
            new Box { Id = "photos", Rules = [new RegexRule(@"^IMG_\d+")] },
            new Box { Id = "apps", Rules = [new KindRule(ItemKind.Shortcut)] },
        ],
    };

    [Theory]
    [InlineData("Report.pdf", "docs")]        // first box wins over "reports"
    [InlineData("letter.DOCX", "docs")]
    [InlineData("monthly report.xlsx", "reports")]
    [InlineData("img_2024.jpg", "photos")]
    [InlineData("pdf", null)]                 // no extension
    [InlineData("already.pdf", null)]         // already has a box
    [InlineData("loose.pdf", null)]           // pulled out on purpose
    [InlineData("notes.txt", null)]
    public void FirstMatchingBoxInOrder(string name, string? expected)
    {
        Assert.Equal(expected, RuleMatcher.Match(Layout, File(name)));
    }

    [Fact]
    public void KindRuleMatchesShortcuts()
    {
        Assert.Equal("apps", RuleMatcher.Match(Layout, File("Editor.lnk", ItemKind.Shortcut)));
    }

    [Fact]
    public void InvalidRegexNeverMatchesOrThrows()
    {
        Assert.False(RuleMatcher.Matches(new RegexRule("(unclosed"), File("unclosed")));
    }

    [Fact]
    public void SlowRegexTimesOut()
    {
        var sw = Stopwatch.StartNew();
        Assert.False(RuleMatcher.Matches(new RegexRule("^(a+)+$"), File(new string('a', 40) + "!")));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"took {sw.Elapsed}");
    }

    [Fact]
    public void AssignNewAppendsMatchesToTheirBoxes()
    {
        var result = RuleMatcher.AssignNew(Layout, [File("b.pdf"), File("notes.txt"), File("IMG_1.png")]);
        Assert.Equal(["already.pdf", "b.pdf"], result.Boxes[0].Items);
        Assert.Equal(["IMG_1.png"], result.Boxes[2].Items);
        Assert.Same(Layout, RuleMatcher.AssignNew(Layout, [File("notes.txt")]));
    }
}
