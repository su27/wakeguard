using WakeGuard.Contracts;

namespace WakeGuard.Core;

public sealed class LeaseCoordinator : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<LeaseKey, Lease> _leases = [];
    private readonly IPowerRequestSink _powerRequestSink;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _leaseTimeout;
    private WakeMode _effectiveMode;
    private bool _disposed;

    public LeaseCoordinator(
        IPowerRequestSink powerRequestSink,
        TimeProvider? timeProvider = null,
        TimeSpan? leaseTimeout = null)
    {
        _powerRequestSink = powerRequestSink ?? throw new ArgumentNullException(nameof(powerRequestSink));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _leaseTimeout = leaseTimeout ?? ProtocolConstants.LeaseTimeout;
        if (_leaseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseTimeout));
        }
    }

    public LeaseSnapshot UpsertLease(
        string userSid,
        Guid clientId,
        WakeMode mode,
        DateTimeOffset? stopAtUtc)
    {
        ValidateIdentity(userSid, clientId);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var key = new LeaseKey(userSid, clientId);
            var now = _timeProvider.GetUtcNow();
            RemoveExpiredLeases(now);
            var hadPreviousLease = _leases.TryGetValue(key, out var previousLease);

            if (mode == WakeMode.Inactive || (stopAtUtc is { } stopAt && stopAt <= now))
            {
                _leases.Remove(key);
            }
            else
            {
                _leases[key] = new Lease(mode, now + _leaseTimeout, stopAtUtc);
            }

            try
            {
                ReconcilePowerMode();
            }
            catch
            {
                RestoreLease(key, hadPreviousLease, previousLease);
                throw;
            }
            return CreateSnapshot(key);
        }
    }

    public LeaseSnapshot ReleaseLease(string userSid, Guid clientId)
    {
        ValidateIdentity(userSid, clientId);
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var key = new LeaseKey(userSid, clientId);
            var hadPreviousLease = _leases.Remove(key, out var previousLease);
            RemoveExpiredLeases(_timeProvider.GetUtcNow());
            try
            {
                ReconcilePowerMode();
            }
            catch
            {
                RestoreLease(key, hadPreviousLease, previousLease);
                throw;
            }
            return CreateSnapshot(key);
        }
    }

    public LeaseSnapshot GetSnapshot(string userSid, Guid clientId)
    {
        ValidateIdentity(userSid, clientId);
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            var key = new LeaseKey(userSid, clientId);
            RemoveExpiredLeases(_timeProvider.GetUtcNow());
            ReconcilePowerMode();
            return CreateSnapshot(key);
        }
    }

    public WakeMode SweepExpiredLeases()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            RemoveExpiredLeases(_timeProvider.GetUtcNow());
            ReconcilePowerMode();
            return _effectiveMode;
        }
    }

    public DateTimeOffset? GetNextExpirationUtc()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return _leases.Count == 0
                ? null
                : _leases.Values.Min(lease => lease.ExpiresAtUtc);
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _leases.Clear();
            _powerRequestSink.ApplyMode(WakeMode.Inactive);
            _effectiveMode = WakeMode.Inactive;
            _disposed = true;
        }
    }

    private void RemoveExpiredLeases(DateTimeOffset now)
    {
        foreach (var key in _leases
                     .Where(pair => pair.Value.ExpiresAtUtc <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _leases.Remove(key);
        }
    }

    private void ReconcilePowerMode()
    {
        var requestedMode = _leases.Count == 0
            ? WakeMode.Inactive
            : _leases.Values.Max(lease => lease.Mode);
        if (requestedMode == _effectiveMode)
        {
            return;
        }

        _powerRequestSink.ApplyMode(requestedMode);
        _effectiveMode = requestedMode;
    }

    private LeaseSnapshot CreateSnapshot(LeaseKey key)
    {
        if (_leases.TryGetValue(key, out var lease))
        {
            return new LeaseSnapshot(
                _effectiveMode,
                lease.Mode,
                lease.StopAtUtc,
                lease.HeartbeatDeadlineUtc,
                _leases.Count);
        }

        return new LeaseSnapshot(_effectiveMode, WakeMode.Inactive, null, null, _leases.Count);
    }

    private void RestoreLease(LeaseKey key, bool hadPreviousLease, Lease? previousLease)
    {
        if (hadPreviousLease)
        {
            _leases[key] = previousLease!;
        }
        else
        {
            _leases.Remove(key);
        }
    }

    private static void ValidateIdentity(string userSid, Guid clientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client identifier must not be empty.", nameof(clientId));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct LeaseKey(string UserSid, Guid ClientId);

    private sealed record Lease(
        WakeMode Mode,
        DateTimeOffset HeartbeatDeadlineUtc,
        DateTimeOffset? StopAtUtc)
    {
        internal DateTimeOffset ExpiresAtUtc => StopAtUtc is { } stopAt && stopAt < HeartbeatDeadlineUtc
            ? stopAt
            : HeartbeatDeadlineUtc;
    }
}
