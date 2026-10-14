namespace GigaDict.App;

internal static class HotkeyParser
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = 0x11, ["LCtrl"] = 0xA2, ["RCtrl"] = 0xA3,
        ["Shift"] = 0x10, ["LShift"] = 0xA0, ["RShift"] = 0xA1,
        ["Alt"] = 0x12, ["LAlt"] = 0xA4, ["RAlt"] = 0xA5,
        ["Space"] = 0x20
    };

    internal static IReadOnlySet<int> Parse(string value)
    {
        var result = new HashSet<int>();
        foreach (var part in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Named.TryGetValue(part, out var named)) result.Add(named);
            else if (part.Length >= 2 && part[0] is 'F' or 'f' && int.TryParse(part[1..], out var function) && function is >= 1 and <= 24)
                result.Add(0x6F + function);
            else if (part.Length == 1 && char.IsLetterOrDigit(part[0])) result.Add(char.ToUpperInvariant(part[0]));
            else throw new FormatException($"Неизвестная клавиша: {part}.");
        }
        if (result.Count == 0) throw new FormatException("Горячая клавиша не задана.");
        return result;
    }
}

