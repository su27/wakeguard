using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using WakeGuard.Contracts;
using WakeGuard.Service;

namespace WakeGuard.Windows.Tests;

public sealed class WakeGuardWindowsServiceTests
{
    [Fact]
    public void IdleServiceRequestsStopAfterGracePeriod()
    {
        using var idleStopRequested = new ManualResetEventSlim();
        using var service = new WakeGuardWindowsService(
            CreatePipeName(),
            idleStopRequested.Set,
            TimeSpan.FromMilliseconds(150));

        service.StartForDiagnostics();
        try
        {
            Assert.True(idleStopRequested.Wait(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            service.StopForDiagnostics();
        }
    }

    [Fact]
    public async Task ActiveLeaseDefersIdleStopUntilLeaseExpires()
    {
        var pipeName = CreatePipeName();
        using var idleStopRequested = new ManualResetEventSlim();
        using var service = new WakeGuardWindowsService(
            pipeName,
            idleStopRequested.Set,
            TimeSpan.FromMilliseconds(200));

        service.StartForDiagnostics();
        try
        {
            using var process = Process.GetCurrentProcess();
            var response = await SendRequestAsync(pipeName, new ServiceRequest
            {
                Kind = RequestKind.UpsertLease,
                ClientId = Guid.NewGuid(),
                SessionId = process.SessionId,
                ProcessId = Environment.ProcessId,
                RequestedMode = WakeMode.KeepAwake,
                StopAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(800),
            });

            Assert.True(response.Success, response.ErrorMessage);
            Assert.False(idleStopRequested.Wait(TimeSpan.FromMilliseconds(450)));
            Assert.True(idleStopRequested.Wait(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            service.StopForDiagnostics();
        }
    }

    private static async Task<ServiceResponse> SendRequestAsync(
        string pipeName,
        ServiceRequest request)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(3_000);
        await PipeMessageSerializer.WriteAsync(pipe, request);
        return await PipeMessageSerializer.ReadAsync<ServiceResponse>(pipe);
    }

    private static string CreatePipeName() => $"WakeGuard.Tests.{Guid.NewGuid():N}";
}
