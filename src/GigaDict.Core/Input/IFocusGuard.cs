using GigaDict.Core.Configuration;
using GigaDict.Core.Tasks;

namespace GigaDict.Core.Input;

public interface IFocusGuard
{
    FocusIdentity? Capture();
    void Track(Guid taskId, FocusIdentity identity, FocusProtectionMode mode = FocusProtectionMode.Strict);
    bool IsStillSafe(Guid taskId);
    void Forget(Guid taskId);
}
