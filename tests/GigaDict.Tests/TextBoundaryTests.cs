using GigaDict.Core.Input;

namespace GigaDict.Tests;

public sealed class TextBoundaryTests
{
    [Theory]
    [InlineData("Как дела?", 'т', null, false, " Как дела?")]
    [InlineData("Как дела?", ' ', null, false, "Как дела?")]
    [InlineData("мир", '\n', null, false, "мир")]
    [InlineData(", мир", 'т', null, false, ", мир")]
    [InlineData("текст", null, '.', false, "текст")]
    [InlineData("текст", 'а', 'б', true, "текст")]
    public void PreparesSafeBoundary(string text, char? before, char? after, bool selection, string expected) =>
        Assert.Equal(expected, TextBoundary.Prepare(text, before, after, selection, true));

    [Theory]
    [InlineData('(', "текст")]
    [InlineData('«', "текст")]
    [InlineData('[', "текст")]
    [InlineData('a', " текст")]
    [InlineData('"', " текст")]
    public void NoSpaceAfterOpeningBracketOrQuote(char before, string expected) =>
        Assert.Equal(expected, TextBoundary.Prepare("текст", before, null, false, true));

    [Fact]
    public void ContextSeparatesConsecutiveDictations() =>
        Assert.Equal(" как дела", TextBoundary.Prepare("как дела", new CaretContext('т', null), true));

    [Fact]
    public void ContextAddsTrailingSpaceBeforeNextWord() =>
        Assert.Equal(" вставка ", TextBoundary.Prepare("вставка", new CaretContext('а', 'б'), true));

    [Fact]
    public void FieldStartNeedsNoLeadingSpace() =>
        Assert.Equal("Привет", TextBoundary.Prepare("Привет", new CaretContext(null, null), true));

    [Fact]
    public void UnknownContextOrDisabledLeavesTextUnchanged()
    {
        Assert.Equal("текст", TextBoundary.Prepare(" текст ", null, true));
        Assert.Equal("текст", TextBoundary.Prepare("текст", new CaretContext('а', 'б'), false));
    }

    [Fact]
    public void PreservesUnicodeOutsideBmp() => Assert.Equal("🎤 Привет", TextBoundary.Prepare("🎤 Привет", null, null, false, true));
}

