using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using WakeGuard.Contracts;

namespace WakeGuard.Tray;

internal sealed class WakeGuardClient
{
    internal Guid ClientId { get; } = Guid.NewGuid();

    internal async Task<ServiceResponse> QueryStatusAsync(CancellationToken cancellationToken = default) =>
        await SendAsync(RequestKind.QueryStatus, WakeMode.Inactive, null, cancellationToken);

    internal async Task<ServiceResponse> UpsertLeaseAsync(
        WakeMode mode,
        DateTimeOffset? stopAtUtc,
        CancellationToken cancellationToken = default) =>
        await SendAsync(RequestKind.UpsertLease, mode, stopAtUtc, cancellationToken);

    internal async Task<ServiceResponse> ReleaseLeaseAsync(CancellationToken cancellationToken = default) =>
        await SendAsync(RequestKind.ReleaseLease, WakeMode.Inactive, null, cancellationToken);

    private async Task<ServiceResponse> SendAsync(
        RequestKind kind,
        WakeMode mode,
        DateTimeOffset? stopAtUtc,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProtocolConstants.ConnectTimeout + TimeSpan.FromSeconds(2));
        await using var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: ProtocolConstants.PipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous,
            impersonationLevel: TokenImpersonationLevel.Identification);

        try
        {
            await pipe.ConnectAsync((int)ProtocolConstants.ConnectTimeout.TotalMilliseconds, timeout.Token)
                .ConfigureAwait(false);
            var request = new ServiceRequest
            {
                Kind = kind,
                ClientId = ClientId,
                SessionId = Process.GetCurrentProcess().SessionId,
                ProcessId = Environment.ProcessId,
                RequestedMode = mode,
                StopAtUtc = stopAtUtc?.ToUniversalTime(),
            };
            await PipeMessageSerializer.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
            return await PipeMessageSerializer
                .ReadAsync<ServiceResponse>(pipe, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("WakeGuard 后台服务没有在规定时间内响应。");
        }
    }
}
