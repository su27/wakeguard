using WakeGuard.Contracts;

namespace WakeGuard.Core;

public interface IPowerRequestSink
{
    void ApplyMode(WakeMode mode);
}
