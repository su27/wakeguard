using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WakeGuard.Tray;

internal static class SessionActions
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    internal static void Lock()
    {
        if (!LockWorkStation())
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 无法锁定当前工作站");
        }
    }

    internal static void StartScreenSaver()
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
        var configuredPath = desktop?.GetValue("SCRNSAVE.EXE") as string;
        var screenSaverPath = ResolveScreenSaver(configuredPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = screenSaverPath,
            Arguments = "/s",
            UseShellExecute = true,
        });
    }

    private static string ResolveScreenSaver(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expandedPath = Environment
                .ExpandEnvironmentVariables(configuredPath)
                .Trim()
                .Trim('"');
            if (File.Exists(expandedPath))
            {
                return expandedPath;
            }
        }

        var fallback = Path.Combine(Environment.SystemDirectory, "scrnsave.scr");
        if (!File.Exists(fallback))
        {
            throw new FileNotFoundException("Windows 没有配置屏幕保护程序，也找不到内置黑屏屏保。", fallback);
        }

        return fallback;
    }
}
