namespace Orange.Desktop.Engine;

/// <summary>Parses "Ctrl+Alt+D" style text into RegisterHotKey modifiers and a virtual key.</summary>
public static class Hotkey
{
    public const uint ModAlt = 0x1, ModCtrl = 0x2, ModShift = 0x4, ModWin = 0x8;

    public static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.ToUpperInvariant();
            uint mod = part switch
            {
                "CTRL" or "CONTROL" => ModCtrl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                "WIN" or "WINDOWS" => ModWin,
                _ => 0,
            };
            if (mod != 0) { modifiers |= mod; continue; }

            if (vk != 0) return false;   // two keys
            vk = KeyCode(part);
            if (vk == 0) return false;
        }

        // A plain letter would steal normal typing; only F-keys may go without modifiers.
        bool isFKey = vk is >= 0x70 and <= 0x87;
        return vk != 0 && (modifiers != 0 || isFKey);
    }

    private static uint KeyCode(string part)
    {
        if (part.Length == 1 && part[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') return part[0];
        if (part.Length >= 2 && part[0] == 'F' && int.TryParse(part[1..], out int n) && n is >= 1 and <= 24)
            return (uint)(0x70 + n - 1);
        return 0;
    }
}
