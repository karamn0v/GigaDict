using GigaDict.Core.Configuration;

namespace GigaDict.Tests;

public sealed class SettingsValidationTests
{
    [Fact]
    public void DefaultsAreValid() => Assert.Empty(new AppSettings().Validate());

    [Theory]
    [InlineData("Esc")]
    [InlineData("")]
    [InlineData("Ctrl+Bogus")]
    public void UnparsableHotkeyIsRejected(string hotkey) =>
        Assert.NotEmpty((new AppSettings() with { Hotkey = hotkey }).Validate());

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void HistorySizeOutOfRangeIsRejected(int size) =>
        Assert.NotEmpty((new AppSettings() with { HistorySize = size }).Validate());

    [Fact]
    public void InvalidRetryDelaysAreRejected()
    {
        Assert.NotEmpty((new AppSettings() with { Retry = new RetrySettings { InitialDelaySeconds = -1 } }).Validate());
        Assert.NotEmpty((new AppSettings() with { Retry = new RetrySettings { InitialDelaySeconds = 10, MaximumDelaySeconds = 5 } }).Validate());
    }

    [Fact]
    public void RelativeServerUriIsRejectedWithoutThrowing() =>
        Assert.NotEmpty((new AppSettings() with { ServerBaseUri = new Uri("relative", UriKind.Relative) }).Validate());

    [Fact]
    public async Task FileFromVersionWithWebSocketTransportStillLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gigadict-settings-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """{ "serverBaseUri": "http://nas:9876", "transport": "WebSocket", "hotkey": "F9" }""");
        try
        {
            var settings = await new SettingsStore(path).LoadAsync();
            Assert.Equal(new Uri("http://nas:9876"), settings.ServerBaseUri);
            Assert.Equal("F9", settings.Hotkey);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("""{ "hotkey": "Esc" }""")]
    [InlineData("""{ "hotkey": null }""")]
    [InlineData("""{ "historySize": -1 }""")]
    [InlineData("""{ "retry": null }""")]
    [InlineData("""{ "serverBaseUri": null }""")]
    public async Task ManuallyBrokenFileIsRejectedOnLoad(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gigadict-settings-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        try { await Assert.ThrowsAsync<InvalidDataException>(() => new SettingsStore(path).LoadAsync()); }
        finally { File.Delete(path); }
    }
}
