using System.Text;
using System.Globalization;

namespace WakeGuard.Service;

internal static class ServiceLog
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "WakeGuard");
    private static readonly string LogPath = Path.Combine(LogDirectory, "service.log");

    internal static void Information(string message) => Write("INF", message, null);

    internal static void Error(string message, Exception exception) => Write("ERR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                var builder = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(level)
                    .Append(' ')
                    .Append(message);
                if (exception is not null)
                {
                    builder.AppendLine().Append(exception);
                }

                File.AppendAllText(LogPath, builder.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must not change power-request behavior.
        }
    }
}
