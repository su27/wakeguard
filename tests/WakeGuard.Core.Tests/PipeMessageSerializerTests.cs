using WakeGuard.Contracts;

namespace WakeGuard.Core.Tests;

public sealed class PipeMessageSerializerTests
{
    [Fact]
    public async Task RoundTripPreservesRequest()
    {
        var expected = new ServiceRequest
        {
            Kind = RequestKind.UpsertLease,
            ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SessionId = 4,
            ProcessId = 1234,
            RequestedMode = WakeMode.KeepAwakeAndDisplayOn,
            StopAtUtc = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero),
        };
        await using var stream = new MemoryStream();

        await PipeMessageSerializer.WriteAsync(stream, expected);
        stream.Position = 0;
        var actual = await PipeMessageSerializer.ReadAsync<ServiceRequest>(stream);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReadAsyncRejectsOversizedMessageBeforeAllocation()
    {
        await using var stream = new MemoryStream();
        var header = BitConverter.GetBytes(ProtocolConstants.MaximumMessageBytes + 1);
        await stream.WriteAsync(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await PipeMessageSerializer.ReadAsync<ServiceRequest>(stream));
    }

    [Fact]
    public async Task RoundTripPreservesResponse()
    {
        var expected = new ServiceResponse
        {
            Success = true,
            EffectiveMode = WakeMode.KeepAwake,
            ClientMode = WakeMode.KeepAwake,
            StopAtUtc = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            ServerTimeUtc = new DateTimeOffset(2026, 8, 3, 11, 0, 0, TimeSpan.Zero),
            LeaseDeadlineUtc = new DateTimeOffset(2026, 8, 3, 11, 1, 15, TimeSpan.Zero),
            ActiveLeaseCount = 1,
        };
        await using var stream = new MemoryStream();

        await PipeMessageSerializer.WriteAsync(stream, expected);
        stream.Position = 0;
        var actual = await PipeMessageSerializer.ReadAsync<ServiceResponse>(stream);

        Assert.Equal(expected, actual);
    }
}
