using System.Text.Json;
using Orange.Desktop.Model;
using Orange.Desktop.Store;

namespace Orange.Tests.Desktop;

public sealed class DesktopStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orange-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    public DesktopStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string FilePath => Path.Combine(_dir, "desktop.json");
    private DesktopStore NewStore() => new(FilePath, _log.Add);

    private static readonly MonitorKey Left = new(@"\\?\DISPLAY#AAA0001#1", "Monitor A");
    private static readonly MonitorKey Middle = new(@"\\?\DISPLAY#BBB0002#2", "Monitor B");
    private static readonly MonitorKey Right = new(@"\\?\DISPLAY#CCC0003#3", "Monitor C");

    private static DesktopConfig Sample() => new()
    {
        ActiveLayout = "Work",
        Layouts =
        [
            new DesktopLayout
            {
                Name = "Work",
                LooseItems = ["Loose.txt"],
                Boxes =
                [
                    new Box
                    {
                        Id = "b1", Title = "Apps", Monitor = Left, Rect = new RelRect(0.02, 0.05, 0.2, 0.4),
                        Sort = BoxSort.Name, Items = ["Editor.lnk", "Browser.lnk"],
                        Rules = [new KindRule(ItemKind.Shortcut), new ExtensionRule(".lnk;url")],
                    },
                    new Box
                    {
                        Id = "b2", Title = "Docs", Color = "#1E3A5F", Monitor = Middle, Rect = new RelRect(0.5, 0.1, 0.3, 0.5),
                        Sort = BoxSort.Type, Items = ["Report.pdf"],
                        Rules = [new ExtensionRule(".pdf;.docx"), new NameContainsRule("invoice")],
                    },
                    new Box
                    {
                        Id = "b3", Title = "Images", Monitor = Right, Rect = new RelRect(0.7, 0.6, 0.25, 0.3),
                        Sort = BoxSort.Manual, Items = ["b.png", "a.png"],
                        Rules = [new RegexRule(@"^IMG_\d+"), new KindRule(ItemKind.Folder), new KindRule(ItemKind.File)],
                    },
                ],
            },
            new DesktopLayout { Name = "Home" },
        ],
    };

    private static string Serialize(DesktopConfig c) => JsonSerializer.Serialize(c, DesktopStore.Json);

    [Fact]
    public void RoundTripKeepsEverything()
    {
        var store = NewStore();
        var original = Sample();

        Assert.True(store.Save(original));
        var loaded = store.Load();

        Assert.Equal(Serialize(original), Serialize(loaded));
        Assert.Empty(_log);

        var boxes = loaded.Active!.Boxes;
        Assert.Equal([Left, Middle, Right], boxes.Select(b => b.Monitor));
        Assert.Equal(new RelRect(0.5, 0.1, 0.3, 0.5), boxes[1].Rect);
        Assert.Equal(["b.png", "a.png"], boxes[2].Items);
        Assert.IsType<RegexRule>(boxes[2].Rules[0]);
        Assert.Equal(ItemKind.Folder, Assert.IsType<KindRule>(boxes[2].Rules[1]).Value);
        Assert.Equal(".pdf;.docx", Assert.IsType<ExtensionRule>(boxes[1].Rules[0]).Value);
        Assert.Equal("invoice", Assert.IsType<NameContainsRule>(boxes[1].Rules[1]).Value);
    }

    [Fact]
    public void JsonUsesReadableNames()
    {
        var json = Serialize(Sample());
        Assert.Contains("\"kind\": \"Extension\"", json);
        Assert.Contains("\"sort\": \"Manual\"", json);
        Assert.Contains("\"value\": \"Shortcut\"", json);
    }

    [Fact]
    public void SaveLeavesNoTempFile()
    {
        var store = NewStore();
        store.Save(Sample());
        store.Save(Sample() with { ActiveLayout = "Home" });

        Assert.Equal("Home", store.Load().ActiveLayout);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void MissingFileGivesEmptyConfig()
    {
        var c = NewStore().Load();
        Assert.Empty(c.Layouts);
        Assert.Null(c.Active);
        Assert.Empty(_log);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"layouts\": [{\"name\": \"A\", \"boxes\": [{\"rules\": [{\"kind\": \"Teleport\"}]}]}]}")]
    public void CorruptFileIsSetAsideWithoutThrowing(string content)
    {
        File.WriteAllText(FilePath, content);

        var c = NewStore().Load();

        Assert.Empty(c.Layouts);
        Assert.Single(_log);
        Assert.False(File.Exists(FilePath));
        Assert.Equal(content, File.ReadAllText(FilePath + ".bad"));
    }

    [Fact]
    public void SanitizeRepairsHandEditedValues()
    {
        File.WriteAllText(FilePath, """
        {
          "activeLayout": "Gone",
          "layouts": [
            { "name": "A", "looseItems": ["x.txt", "free.txt"], "boxes": [
              { "id": "same", "items": ["x.txt", "y.txt"], "rect": { "x": 0.95, "y": -1, "w": 5, "h": 0.001 }, "monitor": null },
              { "id": "same", "items": ["X.TXT", "z.txt", null], "rules": null }
            ]},
            { "name": "A" },
            { "name": "" }
          ]
        }
        """);

        var c = NewStore().Load();

        Assert.Equal("A", c.ActiveLayout);
        var layout = Assert.Single(c.Layouts);
        Assert.Equal(["free.txt"], layout.LooseItems);

        var (first, second) = (layout.Boxes[0], layout.Boxes[1]);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(["x.txt", "y.txt"], first.Items);
        Assert.Equal(["z.txt"], second.Items);   // x.txt already belongs to the first box
        Assert.Empty(second.Rules);
        Assert.Equal(MonitorKey.Unknown, first.Monitor);

        // Rect is kept inside the monitor and above a minimum size.
        Assert.Equal(new RelRect(0, 0, 1, 0.02), first.Rect);
    }
}
