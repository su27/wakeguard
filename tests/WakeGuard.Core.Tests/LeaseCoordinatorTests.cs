using WakeGuard.Contracts;
using WakeGuard.Core;

namespace WakeGuard.Core.Tests;

public sealed class LeaseCoordinatorTests
{
    private static readonly Guid ClientA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void UpsertLeaseAppliesRequestedMode()
    {
        var sink = new RecordingPowerSink();
        using var coordinator = CreateCoordinator(sink);

        var snapshot = coordinator.UpsertLease("S-1-5-21-100", ClientA, WakeMode.KeepAwake, null);

        Assert.Equal(WakeMode.KeepAwake, snapshot.EffectiveMode);
        Assert.Equal(WakeMode.KeepAwake, snapshot.ClientMode);
        Assert.Equal([WakeMode.KeepAwake], sink.AppliedModes);
    }

    [Fact]
    public void StrongestLeaseWinsAndReleaseDowngradesMode()
    {
        var sink = new RecordingPowerSink();
        using var coordinator = CreateCoordinator(sink);
        coordinator.UpsertLease("S-1-5-21-100", ClientA, WakeMode.KeepAwake, null);
        coordinator.UpsertLease("S-1-5-21-200", ClientB, WakeMode.KeepAwakeAndDisplayOn, null);

        var snapshot = coordinator.ReleaseLease("S-1-5-21-200", ClientB);

        Assert.Equal(WakeMode.KeepAwake, snapshot.EffectiveMode);
        Assert.Equal(1, snapshot.ActiveLeaseCount);
        Assert.Equal(
            [WakeMode.KeepAwake, WakeMode.KeepAwakeAndDisplayOn, WakeMode.KeepAwake],
            sink.AppliedModes);
    }

    [Fact]
    public void ReleaseLeaseCannotReleaseAnotherUsersLease()
    {
        var sink = new RecordingPowerSink();
        using var coordinator = CreateCoordinator(sink);
        coordinator.UpsertLease("S-1-5-21-100", ClientA, WakeMode.KeepAwake, null);

        var snapshot = coordinator.ReleaseLease("S-1-5-21-999", ClientA);

        Assert.Equal(WakeMode.KeepAwake, snapshot.EffectiveMode);
        Assert.Equal(1, snapshot.ActiveLeaseCount);
    }

    [Fact]
    public void SweepExpiredLeasesReleasesPowerRequestAfterHeartbeatTimeout()
    {
        var time = new ManualTimeProvider();
        var sink = new RecordingPowerSink();
        using var coordinator = CreateCoordinator(sink, time);
        coordinator.UpsertLease("S-1-5-21-100", ClientA, WakeMode.KeepAwake, null);

        time.Advance(TimeSpan.FromSeconds(76));
        var mode = coordinator.SweepExpiredLeases();

        Assert.Equal(WakeMode.Inactive, mode);
        Assert.Equal([WakeMode.KeepAwake, WakeMode.Inactive], sink.AppliedModes);
    }

    [Fact]
    public void HeartbeatRenewsLeaseDeadline()
    {
        var time = new ManualTimeProvider();
        var sink = new RecordingPowerSink();
        using var coordinator = CreateCoordinator(sink, time);
        coordinator.UpsertLease("S-1-5-21-100", ClientA, WakeMode.KeepAwake, null);
        time.Advance(TimeSpan.FromSeconds(60));

        coordinator.UpsertLease("S-1-5-21-100", ClientA, WakeMode.KeepAwake, null);
        time.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(WakeMode.KeepAwake, coordinator.SweepExpiredLeases());
        Assert.Equal([WakeMode.KeepAwake], sink.AppliedModes);
    }

    [Fact]
    public void NextExpirationTracksTheEarliestLeaseDeadline()
    {
        var time = new ManualTimeProvider();
        var sink = new RecordingPowerSink();
        using var coordinator = CreateCoordinator(sink, time);
        var earlierStop = time.GetUtcNow().AddSeconds(30);

        coordinator.UpsertLease("S-1-5-21-100", ClientA, WakeMode.KeepAwake, null);
        coordinator.UpsertLease("S-1-5-21-200", ClientB, WakeMode.KeepAwake, earlierStop);

        Assert.Equal(earlierStop, coordinator.GetNextExpirationUtc());

        coordinator.ReleaseLease("S-1-5-21-200", ClientB);
        Assert.Equal(time.GetUtcNow().AddSeconds(75), coordinator.GetNextExpirationUtc());

        coordinator.ReleaseLease("S-1-5-21-100", ClientA);
        Assert.Null(coordinator.GetNextExpirationUtc());
    }

    [Fact]
    public void UserStopTimeExpiresBeforeHeartbeatDeadline()
    {
        var time = new ManualTimeProvider();
        var sink = new RecordingPowerSink();
        using var coordinator = CreateCoordinator(sink, time);
        var stopAt = time.GetUtcNow().AddMinutes(30);
        coordinator.UpsertLease("S-1-5-21-100", ClientA, WakeMode.KeepAwake, stopAt);

        for (var minute = 0; minute < 30; minute++)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            if (minute < 29)
            {
                coordinator.UpsertLease("S-1-5-21-100", ClientA, WakeMode.KeepAwake, stopAt);
            }
        }

        Assert.Equal(WakeMode.Inactive, coordinator.SweepExpiredLeases());
    }

    [Fact]
    public void PowerApiFailureDoesNotClaimUnappliedMode()
    {
        var sink = new RecordingPowerSink { FailureMode = WakeMode.KeepAwakeAndDisplayOn };
        using var coordinator = CreateCoordinator(sink);

        Assert.Throws<InvalidOperationException>(() =>
            coordinator.UpsertLease(
                "S-1-5-21-100",
                ClientA,
                WakeMode.KeepAwakeAndDisplayOn,
                null));

        sink.FailureMode = null;
        var snapshot = coordinator.GetSnapshot("S-1-5-21-100", ClientA);
        Assert.Equal(WakeMode.Inactive, snapshot.EffectiveMode);
        Assert.Equal(WakeMode.Inactive, snapshot.ClientMode);
    }

    private static LeaseCoordinator CreateCoordinator(
        RecordingPowerSink sink,
        TimeProvider? timeProvider = null) =>
        new(sink, timeProvider, TimeSpan.FromSeconds(75));

    private sealed class RecordingPowerSink : IPowerRequestSink
    {
        internal List<WakeMode> AppliedModes { get; } = [];

        internal WakeMode? FailureMode { get; set; }

        public void ApplyMode(WakeMode mode)
        {
            if (mode == FailureMode)
            {
                throw new InvalidOperationException("Simulated power API failure.");
            }

            AppliedModes.Add(mode);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan value) => _utcNow += value;
    }
}
