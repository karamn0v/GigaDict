namespace GigaDict.Core.Input;

public static class TextBoundary
{
    private static readonly HashSet<char> ClosingPunctuation = [',', '.', '!', '?', ':', ';', ')', ']', '}', '»'];

    public static string Prepare(string recognized, char? beforeCaret, char? afterCaret, bool hasSelection, bool enabled)
    {
        var text = recognized.Trim();
        if (!enabled || hasSelection || text.Length == 0) return text;
        var needsLeading = beforeCaret is { } before && !char.IsWhiteSpace(before) && before is not ('\r' or '\n') && !ClosingPunctuation.Contains(text[0]);
        var needsTrailing = afterCaret is { } after && !char.IsWhiteSpace(after) && after is not ('\r' or '\n') && !ClosingPunctuation.Contains(after);
        return (needsLeading ? " " : "") + text + (needsTrailing ? " " : "");
    }
}
