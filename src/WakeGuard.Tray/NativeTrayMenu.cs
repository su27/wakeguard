using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WakeGuard.Tray;

internal static class NativeTrayMenu
{
    private const uint MfString = 0x0000;
    private const uint MfGrayed = 0x0001;
    private const uint MfDisabled = 0x0002;
    private const uint MfChecked = 0x0008;
    private const uint MfPopup = 0x0010;
    private const uint MfSeparator = 0x0800;
    private const uint MfDefault = 0x1000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNonotify = 0x0080;
    private const uint TpmReturnCommand = 0x0100;
    private const uint WmNull = 0x0000;

    internal enum Command : uint
    {
        None = 0,
        Status = 1000,
        KeepAwake,
        KeepDisplayOn,
        Lock,
        ScreenSaver,
        Stop,
        Timer30Minutes,
        Timer1Hour,
        Timer2Hours,
        Timer4Hours,
        Exit,
    }

    internal readonly record struct State(
        string StatusText,
        bool KeepAwakeChecked,
        bool KeepDisplayOnChecked,
        bool ActiveCommandsEnabled,
        string TimerText);

    internal static Command Show(nint owner, State state)
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            BuildMenu(menu, state);
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

    private static void BuildMenu(nint menu, State state)
    {
        AppendItem(menu, Command.Status, state.StatusText, MfGrayed | MfDisabled | MfDefault);
        AppendSeparator(menu);
        AppendItem(menu, Command.KeepAwake, "保持唤醒", state.KeepAwakeChecked ? MfChecked : 0);
        AppendItem(
            menu,
            Command.KeepDisplayOn,
            "保持唤醒 · 屏幕常亮",
            state.KeepDisplayOnChecked ? MfChecked : 0);
        AppendItem(menu, Command.Lock, "保持唤醒 · 立刻锁屏");
        AppendItem(menu, Command.ScreenSaver, "保持唤醒 · 播放屏保");
        AppendSeparator(menu);

        var disabled = state.ActiveCommandsEnabled ? 0U : MfGrayed | MfDisabled;
        AppendItem(menu, Command.Stop, "退出唤醒状态", disabled);

        var timerMenu = CreatePopupMenu();
        if (timerMenu == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var submenuAttached = false;
        try
        {
            AppendItem(timerMenu, Command.Timer30Minutes, "30 分钟");
            AppendItem(timerMenu, Command.Timer1Hour, "1 小时");
            AppendItem(timerMenu, Command.Timer2Hours, "2 小时");
            AppendItem(timerMenu, Command.Timer4Hours, "4 小时");
            AppendSubmenu(menu, timerMenu, state.TimerText, disabled);
            submenuAttached = true;
        }
        finally
        {
            if (!submenuAttached)
            {
                _ = DestroyMenu(timerMenu);
            }
        }

        AppendSeparator(menu);
        AppendItem(menu, Command.Exit, "退出");
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

    private static void AppendSubmenu(nint menu, nint submenu, string text, uint state)
    {
        if (!AppendMenu(menu, MfPopup | state, (nuint)submenu, text))
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
