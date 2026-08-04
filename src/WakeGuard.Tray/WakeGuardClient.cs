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
        using var process = Process.GetCurrentProcess();
        var request = new ServiceRequest
        {
            Kind = kind,
            ClientId = ClientId,
            SessionId = process.SessionId,
            ProcessId = Environment.ProcessId,
            RequestedMode = mode,
            StopAtUtc = stopAtUtc?.ToUniversalTime(),
        };

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var response = await SendOnceAsync(request, timeout.Token).ConfigureAwait(false);
                    if (!response.Success && response.ErrorCode == "service_stopping")
                    {
                        await DelayBeforeRetryAsync(attempt, timeout.Token).ConfigureAwait(false);
                        continue;
                    }

                    return response;
                }
                catch (IOException) when (!timeout.IsCancellationRequested)
                {
                    await DelayBeforeRetryAsync(attempt, timeout.Token).ConfigureAwait(false);
                }
                catch (TimeoutException) when (!timeout.IsCancellationRequested)
                {
                    await DelayBeforeRetryAsync(attempt, timeout.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(UiText.Current.ServiceTimeout);
        }
    }

    private static async Task<ServiceResponse> SendOnceAsync(
        ServiceRequest request,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: ProtocolConstants.PipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous,
            impersonationLevel: TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(
                (int)ProtocolConstants.ConnectTimeout.TotalMilliseconds,
                cancellationToken)
            .ConfigureAwait(false);
        await PipeMessageSerializer.WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        return await PipeMessageSerializer
            .ReadAsync<ServiceResponse>(pipe, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMilliseconds = Math.Min(100 * (attempt + 1), 500);
        return Task.Delay(delayMilliseconds, cancellationToken);
    }
}
