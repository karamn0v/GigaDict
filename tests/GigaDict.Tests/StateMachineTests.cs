using GigaDict.Core.Configuration;
using GigaDict.Core.Tasks;

namespace GigaDict.Tests;

public sealed class StateMachineTests
{
    [Fact]
    public void ValidPathIsAccepted()
    {
        using var task = NewTask();
        task.TransitionTo(DictationStatus.Queued);
        task.TransitionTo(DictationStatus.Recognizing);
        task.TransitionTo(DictationStatus.Recognized);
        task.TransitionTo(DictationStatus.WaitingForInsertion);
        task.TransitionTo(DictationStatus.Inserted);
        task.TransitionTo(DictationStatus.SavedToHistory);
        Assert.Equal(DictationStatus.SavedToHistory, task.Status);
    }

    [Fact]
    public void InvalidTransitionIsRejected()
    {
        using var task = NewTask();
        Assert.Throws<InvalidOperationException>(() => task.TransitionTo(DictationStatus.Inserted));
    }

    [Fact]
    public void PasswordFocusDisablesInsertion()
    {
        using var task = new DictationTask(1, [0, 0], new FocusIdentity(1, 2, "x", true, true), new AppSettings());
        Assert.False(task.AutomaticInsertionAllowed);
    }

    [Fact]
    public void CompatibleModeAllowsUnknownNonPasswordElement()
    {
        var settings = new AppSettings { FocusProtection = FocusProtectionMode.Compatible };
        using var task = new DictationTask(1, [0, 0], new FocusIdentity(1, 2, "x", false, false), settings);
        Assert.True(task.AutomaticInsertionAllowed);
    }

    private static DictationTask NewTask() => new(1, [0, 0], null, new AppSettings());
}

