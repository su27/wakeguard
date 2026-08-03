using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WakeGuard.Windows;

internal static class NativePowerMethods
{
    private const uint ContextVersion = 0;
    private const uint SimpleStringContext = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext
    {
        internal uint Version;
        internal uint Flags;

        [MarshalAs(UnmanagedType.LPWStr)]
        internal string SimpleReasonString;
    }

    internal enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
        ExecutionRequired = 3,
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PowerSetRequest(
        SafeFileHandle powerRequest,
        PowerRequestType requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PowerClearRequest(
        SafeFileHandle powerRequest,
        PowerRequestType requestType);

    internal static SafeFileHandle CreatePowerRequest(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var context = new ReasonContext
        {
            Version = ContextVersion,
            Flags = SimpleStringContext,
            SimpleReasonString = reason,
        };
        return PowerCreateRequest(ref context);
    }
}
