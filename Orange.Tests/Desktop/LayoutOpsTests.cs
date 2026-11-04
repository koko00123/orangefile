using Orange.Desktop.Layout;
using Orange.Desktop.Model;

namespace Orange.Tests.Desktop;

public class LayoutOpsTests
{
    private static readonly DesktopConfig TwoLayouts = new()
    {
        ActiveLayout = "Home",
        Layouts =
        [
            new DesktopLayout { Name = "Home", Hotkey = "Ctrl+Alt+1", Boxes = [new Box { Id = "h", Title = "Games", Items = ["a.lnk"] }] },
            new DesktopLayout { Name = "Work", Boxes = [new Box { Id = "w", Title = "Docs" }] },
        ],
    };

    [Fact]
    public void SwitchChangesTheActiveLayoutIgnoringCase()
    {
        Assert.Equal("Work", LayoutOps.Switch(TwoLayouts, "work")!.ActiveLayout);
        Assert.Null(LayoutOps.Switch(TwoLayouts, "Missing"));
    }

    [Fact]
    public void SaveAsCopiesTheActiveLayoutWithoutItsShortcut()
    {
        var c = LayoutOps.SaveAs(TwoLayouts, "  Travel ")!;

        Assert.Equal("Travel", c.ActiveLayout);
        Assert.Equal(3, c.Layouts.Count);
        var copy = c.Active!;
        Assert.Equal("Games", copy.Boxes.Single().Title);
        Assert.Equal(["a.lnk"], copy.Boxes.Single().Items);
        Assert.Equal("", copy.Hotkey);
        Assert.Equal("Ctrl+Alt+1", c.Layouts[0].Hotkey);   // the original keeps it
    }

    [Fact]
    public void SaveAsOnAnEmptyConfigKeepsTheCurrentDefaultToo()
    {
        var c = LayoutOps.SaveAs(DesktopConfig.Empty, "Work")!;
        Assert.Equal(["Default", "Work"], c.Layouts.Select(l => l.Name));
        Assert.Equal("Work", c.ActiveLayout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("HOME")]   // taken, case-insensitive
    public void SaveAsRejectsEmptyOrTakenNames(string name)
    {
        Assert.Null(LayoutOps.SaveAs(TwoLayouts, name));
    }

    [Fact]
    public void RenameFollowsTheActiveLayout()
    {
        var c = LayoutOps.Rename(TwoLayouts, "Home", "House")!;
        Assert.Equal("House", c.ActiveLayout);
        Assert.Equal(["House", "Work"], c.Layouts.Select(l => l.Name));

        Assert.Null(LayoutOps.Rename(TwoLayouts, "Home", "work"));          // taken
        Assert.NotNull(LayoutOps.Rename(TwoLayouts, "Home", "HOME"));       // same layout, new case
    }

    [Fact]
    public void DeleteNeverRemovesTheLastLayout()
    {
        var c = LayoutOps.Delete(TwoLayouts, "Home")!;
        Assert.Equal("Work", c.ActiveLayout);
        Assert.Single(c.Layouts);
        Assert.Null(LayoutOps.Delete(c, "Work"));
    }

    [Fact]
    public void AShortcutBelongsToOneLayoutOnly()
    {
        var c = LayoutOps.SetHotkey(TwoLayouts, "Work", "ctrl+alt+1")!;
        Assert.Equal("", c.Layouts[0].Hotkey);
        Assert.Equal("ctrl+alt+1", c.Layouts[1].Hotkey);

        Assert.Equal("", LayoutOps.SetHotkey(c, "Work", "")!.Layouts[1].Hotkey);
        Assert.Null(LayoutOps.SetHotkey(c, "Work", "Q"));   // invalid
    }
}
