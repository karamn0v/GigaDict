using GigaDict.Core.Configuration;
using GigaDict.Core.Input;
using GigaDict.Core.Tasks;

namespace GigaDict.Tests;

public sealed class FocusContinuityTrackerTests
{
    private static readonly FocusIdentity Original = new(10, 20, "1.2.3", true, false);

    [Fact]
    public void SameElementRemainsSafe()
    {
        var tracker = new FocusContinuityTracker();
        var id = Guid.NewGuid();
        tracker.Track(id, Original);
        tracker.Observe(Original);
        Assert.True(tracker.IsStillSafe(id, Original));
    }

    [Fact]
    public void UnknownFocusPermanentlyInvalidatesTask()
    {
        var tracker = new FocusContinuityTracker();
        var id = Guid.NewGuid();
        tracker.Track(id, Original);
        tracker.Observe(null);
        Assert.False(tracker.IsStillSafe(id, Original));
    }

    [Fact]
    public void ReturningToOriginalElementDoesNotRestoreSafety()
    {
        var tracker = new FocusContinuityTracker();
        var id = Guid.NewGuid();
        tracker.Track(id, Original);
        tracker.Observe(Original with { RuntimeId = "9.9" });
        tracker.Observe(Original);
        Assert.False(tracker.IsStillSafe(id, Original));
    }

    [Fact]
    public void CompatibleModeAllowsAnotherElementInSameWindowAndProcess()
    {
        var tracker = new FocusContinuityTracker();
        var id = Guid.NewGuid();
        tracker.Track(id, Original with { IsEditable = false }, FocusProtectionMode.Compatible);
        var anotherElement = Original with { RuntimeId = "9.9", IsEditable = false };
        tracker.Observe(anotherElement);
        Assert.True(tracker.IsStillSafe(id, anotherElement));
    }

    [Fact]
    public void CompatibleModeRejectsAnotherWindow()
    {
        var tracker = new FocusContinuityTracker();
        var id = Guid.NewGuid();
        tracker.Track(id, Original, FocusProtectionMode.Compatible);
        var anotherWindow = Original with { WindowHandle = 11, RuntimeId = "9.9" };
        tracker.Observe(anotherWindow);
        Assert.False(tracker.IsStillSafe(id, anotherWindow));
    }

    [Fact]
    public void CompatibleModeRejectsPasswordField()
    {
        var tracker = new FocusContinuityTracker();
        var id = Guid.NewGuid();
        tracker.Track(id, Original, FocusProtectionMode.Compatible);
        var password = Original with { RuntimeId = "9.9", IsPassword = true };
        tracker.Observe(password);
        Assert.False(tracker.IsStillSafe(id, password));
    }
}
