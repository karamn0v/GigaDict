using GigaDict.Core.Configuration;

namespace GigaDict.Tests;

public sealed class AutoStartRulesTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\Programs\GigaDict\GigaDict.exe";

    [Fact]
    public void MissingApprovalRecordMeansEnabled() => Assert.True(AutoStartRules.IsEnabled($"\"{Exe}\"", null));

    [Theory]
    [InlineData(0x02, true)]
    [InlineData(0x06, true)]
    [InlineData(0x03, false)]
    [InlineData(0x07, false)]
    public void TaskManagerDisableIsDetected(byte first, bool enabled)
    {
        var record = new byte[12];
        record[0] = first;
        Assert.Equal(enabled, AutoStartRules.IsEnabled($"\"{Exe}\"", record));
    }

    [Fact]
    public void NoRunEntryMeansDisabledEvenIfApproved() =>
        Assert.False(AutoStartRules.IsEnabled(null, [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

    [Fact]
    public void MovedExeNeedsPathUpdate()
    {
        Assert.True(AutoStartRules.NeedsPathUpdate(@"""D:\old\GigaDict.exe""", Exe));
        Assert.False(AutoStartRules.NeedsPathUpdate($"\"{Exe.ToUpperInvariant()}\" ", Exe));
    }

    [Fact]
    public void DisabledAutoStartIsNeverCreatedByPathRefresh()
    {
        Assert.False(AutoStartRules.NeedsPathUpdate(null, Exe));
        Assert.False(AutoStartRules.NeedsPathUpdate("", Exe));
    }

    [Theory]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\me\GigaDict.exe", true)]
    [InlineData(@"\\nas\share\GigaDict.exe", true)]
    [InlineData(Exe, false)]
    public void NetworkPathIsRecognized(string path, bool expected) =>
        Assert.Equal(expected, AutoStartRules.IsNetworkPath(path));
}
