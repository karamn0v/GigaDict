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

    [Fact]
    public void PreservesUnicodeOutsideBmp() => Assert.Equal("🎤 Привет", TextBoundary.Prepare("🎤 Привет", null, null, false, true));
}

