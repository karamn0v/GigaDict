namespace GigaDict.Core.Input;

// Characters adjacent to the caret (or to the selection that will be replaced).
// Null means the field boundary or that the editor did not report the character.
public sealed record CaretContext(char? Before, char? After);

public static class TextBoundary
{
    private static readonly HashSet<char> ClosingPunctuation = [',', '.', '!', '?', ':', ';', ')', ']', '}', '»'];
    // Straight quotes and “ open or close depending on language and position, so
    // they keep the default (a space is added) rather than guessing.
    private static readonly HashSet<char> OpeningPunctuation = ['(', '[', '{', '«', '„', '/'];

    // Without a reliable context the text is inserted unchanged.
    public static string Prepare(string recognized, CaretContext? context, bool enabled) =>
        Prepare(recognized, context?.Before, context?.After, false, enabled && context is not null);

    public static string Prepare(string recognized, char? beforeCaret, char? afterCaret, bool hasSelection, bool enabled)
    {
        var text = recognized.Trim();
        if (!enabled || hasSelection || text.Length == 0) return text;
        var needsLeading = beforeCaret is { } before && !char.IsWhiteSpace(before) && !OpeningPunctuation.Contains(before) &&
            !ClosingPunctuation.Contains(text[0]);
        var needsTrailing = afterCaret is { } after && !char.IsWhiteSpace(after) && !ClosingPunctuation.Contains(after);
        return (needsLeading ? " " : "") + text + (needsTrailing ? " " : "");
    }
}
