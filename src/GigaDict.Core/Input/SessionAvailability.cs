namespace GigaDict.Core.Input;

// Values match WTS_* codes and Microsoft.Win32.SessionSwitchReason.
public enum SessionChange
{
    ConsoleConnect = 1, ConsoleDisconnect = 2, RemoteConnect = 3, RemoteDisconnect = 4,
    SessionLogon = 5, SessionLogoff = 6, SessionLock = 7, SessionUnlock = 8, SessionRemoteControl = 9
}

public static class SessionAvailability
{
    // Only events that detach or lock the session disable dictation. Unrelated
    // events (e.g. SessionRemoteControl) must not leave the hotkey silently dead
    // until the next unlock. A connected but still locked session shows the
    // secure desktop, where the hook gets no keys and focus capture fails.
    public static bool Next(bool current, SessionChange change) => change switch
    {
        SessionChange.SessionLock or SessionChange.SessionLogoff or
            SessionChange.ConsoleDisconnect or SessionChange.RemoteDisconnect => false,
        SessionChange.SessionUnlock or SessionChange.SessionLogon or
            SessionChange.ConsoleConnect or SessionChange.RemoteConnect => true,
        _ => current
    };
}
