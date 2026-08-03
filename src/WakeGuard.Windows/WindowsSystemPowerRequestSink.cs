using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WakeGuard.Contracts;
using WakeGuard.Core;

namespace WakeGuard.Windows;

public sealed class WindowsSystemPowerRequestSink : IPowerRequestSink, IDisposable
{
    private readonly object _syncRoot = new();
    private readonly HashSet<NativePowerMethods.PowerRequestType> _activeRequests = [];
    private SafeFileHandle? _handle;
    private bool _disposed;

    public void ApplyMode(WakeMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var desiredRequests = mode == WakeMode.Inactive
                ? new HashSet<NativePowerMethods.PowerRequestType>()
                :
                [
                    NativePowerMethods.PowerRequestType.SystemRequired,
                    NativePowerMethods.PowerRequestType.ExecutionRequired,
                ];
            ApplyRequests(desiredRequests);
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var request in _activeRequests.ToArray())
            {
                NativePowerMethods.PowerClearRequest(_handle!, request);
            }

            _activeRequests.Clear();
            ReleaseHandle();
            _disposed = true;
        }
    }

    private void ApplyRequests(HashSet<NativePowerMethods.PowerRequestType> desiredRequests)
    {
        var addedRequests = new List<NativePowerMethods.PowerRequestType>();
        var removedRequests = new List<NativePowerMethods.PowerRequestType>();
        var handle = desiredRequests.Count > 0 || _activeRequests.Count > 0
            ? GetOrCreateHandle()
            : null;

        try
        {
            foreach (var request in desiredRequests.Except(_activeRequests))
            {
                if (!NativePowerMethods.PowerSetRequest(handle!, request))
                {
                    throw CreateWin32Exception($"PowerSetRequest({request}) failed");
                }

                _activeRequests.Add(request);
                addedRequests.Add(request);
            }

            foreach (var request in _activeRequests.Except(desiredRequests).ToArray())
            {
                if (!NativePowerMethods.PowerClearRequest(handle!, request))
                {
                    throw CreateWin32Exception($"PowerClearRequest({request}) failed");
                }

                _activeRequests.Remove(request);
                removedRequests.Add(request);
            }
        }
        catch
        {
            foreach (var request in removedRequests)
            {
                if (NativePowerMethods.PowerSetRequest(handle!, request))
                {
                    _activeRequests.Add(request);
                }
            }

            foreach (var request in addedRequests)
            {
                NativePowerMethods.PowerClearRequest(handle!, request);
                _activeRequests.Remove(request);
            }

            if (_activeRequests.Count == 0)
            {
                ReleaseHandle();
            }

            throw;
        }

        if (_activeRequests.Count == 0)
        {
            ReleaseHandle();
        }
    }

    private SafeFileHandle GetOrCreateHandle()
    {
        if (_handle is not null)
        {
            return _handle;
        }

        var handle = NativePowerMethods.CreatePowerRequest(
            "WakeGuard is keeping background work active");
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw CreateWin32Exception("PowerCreateRequest failed");
        }

        _handle = handle;
        return handle;
    }

    private void ReleaseHandle()
    {
        _handle?.Dispose();
        _handle = null;
    }

    private static Win32Exception CreateWin32Exception(string message) =>
        new(Marshal.GetLastWin32Error(), message);
}
