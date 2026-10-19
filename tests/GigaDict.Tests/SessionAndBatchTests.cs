using GigaDict.Core.Input;

namespace GigaDict.Tests;

public sealed class SessionAndBatchTests
{
    [Theory]
    [InlineData(SessionChange.SessionLock, false)]
    [InlineData(SessionChange.SessionLogoff, false)]
    [InlineData(SessionChange.ConsoleDisconnect, false)]
    [InlineData(SessionChange.RemoteDisconnect, false)]
    [InlineData(SessionChange.SessionUnlock, true)]
    [InlineData(SessionChange.SessionLogon, true)]
    [InlineData(SessionChange.ConsoleConnect, true)]
    [InlineData(SessionChange.RemoteConnect, true)]
    public void LockAndDetachDisableAttachAndUnlockEnable(SessionChange change, bool expected)
    {
        Assert.Equal(expected, SessionAvailability.Next(true, change));
        Assert.Equal(expected, SessionAvailability.Next(false, change));
    }

    [Fact]
    public void UnrelatedSessionEventDoesNotDisableDictation()
    {
        Assert.True(SessionAvailability.Next(true, SessionChange.SessionRemoteControl));
        Assert.False(SessionAvailability.Next(false, SessionChange.SessionRemoteControl));
    }

    [Fact]
    public void ReasonValuesMatchWindowsCodes()
    {
        Assert.Equal(1, (int)SessionChange.ConsoleConnect);
        Assert.Equal(7, (int)SessionChange.SessionLock);
        Assert.Equal(9, (int)SessionChange.SessionRemoteControl);
    }

    [Fact]
    public void BatchesNeverSplitSurrogatePairs()
    {
        var text = new string('a', 63) + "🎤" + new string('b', 70);
        var batches = UnicodeBatches.Split(text, 64).Select(r => text[r]).ToArray();
        Assert.Equal(text, string.Concat(batches));
        Assert.All(batches, b => Assert.False(char.IsHighSurrogate(b[^1])));
        Assert.All(batches, b => Assert.False(char.IsLowSurrogate(b[0])));
        Assert.All(batches, b => Assert.InRange(b.Length, 1, 64));
    }

    [Fact]
    public void TextOfOnlyEmojiIsSplitIntoWholePairs()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 50));
        var batches = UnicodeBatches.Split(text, 5).Select(r => text[r]).ToArray();
        Assert.Equal(text, string.Concat(batches));
        Assert.All(batches, b => Assert.Equal(0, b.Length % 2));
    }
}
