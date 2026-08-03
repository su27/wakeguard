using System.Text;
using System.Globalization;

namespace WakeGuard.Tray;

internal static class TrayLog
{
    private static readonly object SyncRoot = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WakeGuard",
        "tray.log");

    internal static void Error(string message, Exception exception)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var entry = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                    .Append(" ERR ")
                    .AppendLine(message)
                    .AppendLine(exception.ToString())
                    .ToString();
                File.AppendAllText(LogPath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must not interrupt tray actions.
        }
    }
}
