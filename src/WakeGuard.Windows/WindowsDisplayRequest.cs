using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WakeGuard.Windows;

public sealed class WindowsDisplayRequest : IDisposable
{
    private readonly SafeFileHandle _handle = NativePowerMethods.CreatePowerRequest(
        "WakeGuard is keeping the display on");
    private bool _active;
    private bool _disposed;

    public WindowsDisplayRequest()
    {
        if (_handle.IsInvalid)
        {
            throw CreateWin32Exception("PowerCreateRequest for display failed");
        }
    }

    public bool IsActive => _active;

    public void SetActive(bool active)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (active == _active)
        {
            return;
        }

        var succeeded = active
            ? NativePowerMethods.PowerSetRequest(
                _handle,
                NativePowerMethods.PowerRequestType.DisplayRequired)
            : NativePowerMethods.PowerClearRequest(
                _handle,
                NativePowerMethods.PowerRequestType.DisplayRequired);
        if (!succeeded)
        {
            var operation = active ? "PowerSetRequest" : "PowerClearRequest";
            throw CreateWin32Exception($"{operation}(DisplayRequired) failed");
        }

        _active = active;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_active)
        {
            NativePowerMethods.PowerClearRequest(
                _handle,
                NativePowerMethods.PowerRequestType.DisplayRequired);
            _active = false;
        }

        _handle.Dispose();
        _disposed = true;
    }

    private static Win32Exception CreateWin32Exception(string message) =>
        new(Marshal.GetLastWin32Error(), message);
}
