namespace WakeGuard.Contracts;

public enum RequestKind
{
    QueryStatus,
    UpsertLease,
    ReleaseLease,
}

public sealed record ServiceRequest
{
    public int ProtocolVersion { get; init; } = ProtocolConstants.Version;

    public required RequestKind Kind { get; init; }

    public required Guid ClientId { get; init; }

    public int SessionId { get; init; }

    public int ProcessId { get; init; }

    public WakeMode RequestedMode { get; init; }

    public DateTimeOffset? StopAtUtc { get; init; }
}

public sealed record ServiceResponse
{
    public int ProtocolVersion { get; init; } = ProtocolConstants.Version;

    public bool Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public WakeMode EffectiveMode { get; init; }

    public WakeMode ClientMode { get; init; }

    public DateTimeOffset? StopAtUtc { get; init; }

    public DateTimeOffset ServerTimeUtc { get; init; }

    public DateTimeOffset? LeaseDeadlineUtc { get; init; }

    public int ActiveLeaseCount { get; init; }
}
