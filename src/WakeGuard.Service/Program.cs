using System.ServiceProcess;

namespace WakeGuard.Service;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Contains("--diagnostic", StringComparer.OrdinalIgnoreCase))
        {
            RunDiagnosticHost(ParseDiagnosticDuration(args), ParseDiagnosticPipeName(args));
            return;
        }

        ServiceBase.Run(new WakeGuardWindowsService());
    }

    private static void RunDiagnosticHost(TimeSpan? duration, string? pipeName)
    {
        using var stopped = new ManualResetEventSlim();
        using var service = new WakeGuardWindowsService(pipeName, stopped.Set);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopped.Set();
        };

        service.StartForDiagnostics();
        if (duration is { } value)
        {
            stopped.Wait(value);
        }
        else
        {
            stopped.Wait();
        }
        service.StopForDiagnostics();
    }

    private static TimeSpan? ParseDiagnosticDuration(string[] args)
    {
        var durationArgument = args.FirstOrDefault(value =>
            value.StartsWith("--duration-seconds=", StringComparison.OrdinalIgnoreCase));
        if (durationArgument is null)
        {
            return null;
        }

        var value = durationArgument[(durationArgument.IndexOf('=') + 1)..];
        return int.TryParse(value, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : throw new ArgumentException("Diagnostic duration must be a positive number of seconds.");
    }

    private static string? ParseDiagnosticPipeName(string[] args)
    {
        var pipeArgument = args.FirstOrDefault(value =>
            value.StartsWith("--pipe-name=", StringComparison.OrdinalIgnoreCase));
        if (pipeArgument is null)
        {
            return null;
        }

        var value = pipeArgument[(pipeArgument.IndexOf('=') + 1)..];
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Diagnostic pipe name must not be empty.")
            : value;
    }
}
