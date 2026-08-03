using WakeGuard.Contracts;

namespace WakeGuard.Core;

public sealed record LeaseSnapshot(
    WakeMode EffectiveMode,
    WakeMode ClientMode,
    DateTimeOffset? StopAtUtc,
    DateTimeOffset? LeaseDeadlineUtc,
    int ActiveLeaseCount);
