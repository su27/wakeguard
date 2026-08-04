using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WakeGuard.Tray;

internal static class NativeTrayMenu
{
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint MfDefault = 0x1000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNonotify = 0x0080;
    private const uint TpmReturnCommand = 0x0100;
    private const uint WmNull = 0x0000;

    internal enum Command : uint
    {
        None = 0,
        Settings = 1000,
        Exit,
    }

    internal static Command Show(nint owner, UiText text)
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            BuildMenu(menu, text);
            if (!GetCursorPos(out var location))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _ = SetForegroundWindow(owner);
            var command = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmNonotify | TpmReturnCommand,
                location.X,
                location.Y,
                owner,
                nint.Zero);
            _ = PostMessage(owner, WmNull, nint.Zero, nint.Zero);
            return (Command)command;
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private static void BuildMenu(nint menu, UiText text)
    {
        AppendItem(menu, Command.Settings, text.MenuSettings, MfDefault);
        AppendSeparator(menu);
        AppendItem(menu, Command.Exit, text.MenuExit);
    }

    private static void AppendItem(nint menu, Command command, string text, uint state = 0)
    {
        if (!AppendMenu(menu, MfString | state, (nuint)command, text))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static void AppendSeparator(nint menu)
    {
        if (!AppendMenu(menu, MfSeparator, 0, null))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint identifier, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point location);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        nint menu,
        uint flags,
        int x,
        int y,
        nint owner,
        nint parameters);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        internal readonly int X;
        internal readonly int Y;
    }
}
