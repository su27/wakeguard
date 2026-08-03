using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Principal;
using System.ServiceProcess;
using WakeGuard.Contracts;
using WakeGuard.Core;
using WakeGuard.Windows;

namespace WakeGuard.Service;

internal sealed class WakeGuardWindowsService : ServiceBase
{
    internal const string ServiceNameValue = "WakeGuard";

    private readonly ConcurrentDictionary<Guid, Task> _connections = [];
    private readonly SemaphoreSlim _leaseScheduleChanged = new(0, 1);
    private readonly string _pipeName;
    private CancellationTokenSource? _shutdown;
    private WindowsSystemPowerRequestSink? _powerRequestSink;
    private LeaseCoordinator? _coordinator;
    private Task? _acceptTask;
    private Task? _sweepTask;
    private int _lastLoggedMode = (int)WakeMode.Inactive;

    internal WakeGuardWindowsService(string? pipeName = null)
    {
        _pipeName = pipeName ?? ProtocolConstants.PipeName;
        ServiceName = ServiceNameValue;
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args) => StartCore();

    protected override void OnStop() => StopCore("service stop");

    protected override void OnShutdown()
    {
        StopCore("system shutdown");
        base.OnShutdown();
    }

    internal void StartForDiagnostics() => StartCore();

    internal void StopForDiagnostics() => StopCore("diagnostic host stop");

    private void StartCore()
    {
        // Load identity dependencies before any thread adopts a pipe client's token.
        using var serviceIdentity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        _ = serviceIdentity.User?.Value;
        while (_leaseScheduleChanged.Wait(0))
        {
        }

        _shutdown = new CancellationTokenSource();
        _lastLoggedMode = (int)WakeMode.Inactive;
        _powerRequestSink = new WindowsSystemPowerRequestSink();
        _coordinator = new LeaseCoordinator(_powerRequestSink);
        _acceptTask = AcceptConnectionsAsync(_shutdown.Token);
        _sweepTask = SweepLeasesAsync(_shutdown.Token);
        ServiceLog.Information("WakeGuard service started; no active leases.");
    }

    private void StopCore(string reason)
    {
        var shutdown = Interlocked.Exchange(ref _shutdown, null);
        if (shutdown is null)
        {
            return;
        }

        shutdown.Cancel();
        WaitForTasks([_acceptTask ?? Task.CompletedTask, _sweepTask ?? Task.CompletedTask]);
        WaitForTasks(_connections.Values.ToArray());

        try
        {
            _coordinator?.Dispose();
        }
        catch (Exception exception)
        {
            ServiceLog.Error("Failed to clear the coordinator during shutdown.", exception);
        }

        _coordinator = null;
        _powerRequestSink?.Dispose();
        _powerRequestSink = null;
        shutdown.Dispose();
        ServiceLog.Information($"WakeGuard service stopped: {reason}.");
    }

    private static void WaitForTasks(Task[] tasks)
    {
        try
        {
            Task.WaitAll(tasks, TimeSpan.FromSeconds(5));
        }
        catch (AggregateException exception)
        {
            var unexpected = exception.Flatten().InnerExceptions
                .FirstOrDefault(item => item is not OperationCanceledException);
            if (unexpected is not null)
            {
                ServiceLog.Error("A background task failed while stopping.", unexpected);
            }
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = SecurePipeFactory.CreateServer(_pipeName);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var connectionId = Guid.NewGuid();
                var connectionTask = HandleConnectionAsync(pipe, cancellationToken);
                pipe = null;
                _connections[connectionId] = connectionTask;
                _ = connectionTask.ContinueWith(
                    completedTask => _connections.TryRemove(connectionId, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ServiceLog.Error("Named-pipe accept failed.", exception);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken serviceCancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(serviceCancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var userSid = GetConnectedUserSid(pipe);
                var request = await PipeMessageSerializer
                    .ReadAsync<ServiceRequest>(pipe, timeout.Token)
                    .ConfigureAwait(false);
                var response = ProcessRequest(userSid, request);
                await PipeMessageSerializer.WriteAsync(pipe, response, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (serviceCancellationToken.IsCancellationRequested)
            {
                // Service shutdown closes in-flight requests.
            }
            catch (Exception exception)
            {
                ServiceLog.Error("Named-pipe request failed.", exception);
                await TryWriteErrorAsync(pipe, "request_failed", exception.Message, timeout.Token)
                    .ConfigureAwait(false);
            }
        }
    }

    private ServiceResponse ProcessRequest(string userSid, ServiceRequest request)
    {
        if (request.ProtocolVersion != ProtocolConstants.Version)
        {
            return ErrorResponse(
                "protocol_mismatch",
                $"Unsupported protocol version {request.ProtocolVersion}.");
        }

        if (request.ClientId == Guid.Empty)
        {
            return ErrorResponse("invalid_client", "Client identifier is empty.");
        }

        try
        {
            var coordinator = _coordinator ?? throw new InvalidOperationException("Service is stopping.");
            var snapshot = request.Kind switch
            {
                RequestKind.QueryStatus => coordinator.GetSnapshot(userSid, request.ClientId),
                RequestKind.UpsertLease => coordinator.UpsertLease(
                    userSid,
                    request.ClientId,
                    request.RequestedMode,
                    request.StopAtUtc?.ToUniversalTime()),
                RequestKind.ReleaseLease => coordinator.ReleaseLease(userSid, request.ClientId),
                _ => throw new InvalidOperationException($"Unknown request kind: {request.Kind}."),
            };

            SignalLeaseScheduleChanged();
            LogEffectiveModeChange(snapshot.EffectiveMode);
            return SuccessResponse(snapshot);
        }
        catch (Exception exception)
        {
            ServiceLog.Error(
                $"Request {request.Kind} from SID {userSid}, session {request.SessionId}, process {request.ProcessId} failed.",
                exception);
            return ErrorResponse("operation_failed", exception.Message);
        }
    }

    private async Task SweepLeasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var coordinator = _coordinator;
                if (coordinator is null)
                {
                    return;
                }

                var nextExpirationUtc = coordinator.GetNextExpirationUtc();
                if (nextExpirationUtc is null)
                {
                    await _leaseScheduleChanged.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var delay = nextExpirationUtc.Value - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero &&
                    await _leaseScheduleChanged.WaitAsync(delay, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var mode = coordinator.SweepExpiredLeases();
                LogEffectiveModeChange(mode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        catch (Exception exception)
        {
            ServiceLog.Error("Lease sweep loop failed.", exception);
            throw;
        }
    }

    private void LogEffectiveModeChange(WakeMode mode)
    {
        var previousMode = (WakeMode)Interlocked.Exchange(ref _lastLoggedMode, (int)mode);
        if (previousMode != mode)
        {
            ServiceLog.Information($"Effective wake mode changed to {mode}.");
        }
    }

    private void SignalLeaseScheduleChanged()
    {
        try
        {
            _leaseScheduleChanged.Release();
        }
        catch (SemaphoreFullException)
        {
            // One pending signal is enough to recalculate the next lease deadline.
        }
    }

    private static string GetConnectedUserSid(NamedPipeServerStream pipe)
    {
        string? userSid = null;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            userSid = identity.User?.Value;
        });
        return userSid ?? throw new UnauthorizedAccessException("The pipe client has no user SID.");
    }

    private static ServiceResponse SuccessResponse(LeaseSnapshot snapshot) => new()
    {
        Success = true,
        EffectiveMode = snapshot.EffectiveMode,
        ClientMode = snapshot.ClientMode,
        StopAtUtc = snapshot.StopAtUtc,
        LeaseDeadlineUtc = snapshot.LeaseDeadlineUtc,
        ActiveLeaseCount = snapshot.ActiveLeaseCount,
        ServerTimeUtc = DateTimeOffset.UtcNow,
    };

    private static ServiceResponse ErrorResponse(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
        ServerTimeUtc = DateTimeOffset.UtcNow,
    };

    private static async Task TryWriteErrorAsync(
        Stream pipe,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            if (pipe.CanWrite)
            {
                await PipeMessageSerializer.WriteAsync(pipe, ErrorResponse(code, message), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // The client may have disconnected after sending an invalid request.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _leaseScheduleChanged.Dispose();
        }

        base.Dispose(disposing);
    }
}
