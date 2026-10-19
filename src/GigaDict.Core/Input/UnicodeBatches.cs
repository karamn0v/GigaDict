namespace GigaDict.Core.Input;

public static class UnicodeBatches
{
    // Focus is re-checked between batches. A batch must not end inside a surrogate
    // pair, otherwise aborting there leaves a lone high surrogate in the field.
    public static IEnumerable<Range> Split(string text, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 2);
        for (var offset = 0; offset < text.Length;)
        {
            var length = Math.Min(maximumLength, text.Length - offset);
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length--;
            yield return new Range(offset, offset + length);
            offset += length;
        }
    }
}
