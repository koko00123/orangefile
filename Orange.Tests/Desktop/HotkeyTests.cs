using Orange.Desktop.Engine;

namespace Orange.Tests.Desktop;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", Hotkey.ModCtrl | Hotkey.ModAlt, 'D')]
    [InlineData(" ctrl + shift + 5 ", Hotkey.ModCtrl | Hotkey.ModShift, '5')]
    [InlineData("Win+Alt+F2", Hotkey.ModWin | Hotkey.ModAlt, 0x71)]
    [InlineData("F13", 0u, 0x7C)]
    public void ParsesValidCombinations(string text, uint mods, uint vk)
    {
        Assert.True(Hotkey.TryParse(text, out var m, out var k));
        Assert.Equal(mods, m);
        Assert.Equal(vk, k);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("D")]            // a bare letter would block typing
    [InlineData("Ctrl+Alt")]     // no key
    [InlineData("Ctrl+D+E")]     // two keys
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Space")]   // not supported (yet)
    public void RejectsInvalidCombinations(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _, out _));
    }
}
