using WakeGuard.Contracts;
using WakeGuard.Windows;
using Timer = System.Windows.Forms.Timer;

namespace WakeGuard.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly WakeGuardClient _client = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly Timer _heartbeatTimer;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly ShutdownWindow _shutdownWindow;
    private readonly TrayPopupForm _popup;
    private TraySettings _settings;
    private SettingsForm? _settingsForm;
    private WindowsDisplayRequest? _displayRequest;
    private Icon _currentIcon;
    private TrayIconFactory.IconState _currentIconState = TrayIconFactory.IconState.Disconnected;
    private WakeMode _desiredMode;
    private DateTimeOffset? _stopAtUtc;
    private TimeSpan? _selectedDuration;
    private bool _serviceConnected = true;
    private bool _exiting;
    private string _statusText;

    internal TrayApplicationContext(TraySettings settings)
    {
        _settings = settings;
        _statusText = UiText.Current.StatusInactive;
        _currentIconState = TrayIconFactory.IconState.Inactive;
        _currentIcon = TrayIconFactory.Create(_currentIconState);
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = $"WakeGuard - {_statusText}",
        };

        _heartbeatTimer = new Timer
        {
            Interval = (int)ProtocolConstants.HeartbeatInterval.TotalMilliseconds,
            Enabled = false,
        };
        _heartbeatTimer.Tick += HeartbeatTimerTick;
        _shutdownWindow = new ShutdownWindow(() => ExitAsync(confirmOnFailure: false));
        _popup = new TrayPopupForm(new TrayPopupForm.Actions(
            SetModeFromPanelAsync,
            SetDurationFromPanelAsync,
            LockAsync,
            StartScreenSaverAsync,
            ShowSettings,
            () => ExitAsync(confirmOnFailure: true)));
        _notifyIcon.MouseUp += NotifyIconMouseUp;
        _notifyIcon.Visible = true;
        if (StartupRegistration.IsInstalledExecutable)
        {
            try
            {
                StartupRegistration.SetEnabled(settings.StartWithWindows);
            }
            catch (Exception exception)
            {
                TrayLog.Error("Failed to apply the startup preference.", exception);
            }
        }

        ApplyVisualState();
    }

    private void NotifyIconMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (_exiting)
        {
            return;
        }

        if (eventArgs.Button is MouseButtons.Left or MouseButtons.Right)
        {
            ShowControlPanel();
        }
    }

    private void ShowControlPanel()
    {
        ApplyVisualState();
        _ = RefreshOrRenewAsync(showError: false);
        _popup.ShowAtCursor(CreatePopupState());
    }

    private void ShowSettings()
    {
        _popup.Hide();
        _settingsForm ??= new SettingsForm(_settings, ApplySettings);
        _settingsForm.ShowSettings(_settings);
    }

    private bool ApplySettings(TraySettings settings)
    {
        var startupChanged = settings.StartWithWindows != _settings.StartWithWindows;
        try
        {
            if (startupChanged)
            {
                StartupRegistration.SetEnabled(settings.StartWithWindows);
            }
        }
        catch (Exception exception)
        {
            TrayLog.Error("Failed to update the startup preference.", exception);
            ShowError(UiText.Current.StartupUpdateFailed, exception);
            return false;
        }

        try
        {
            TraySettingsStore.Save(settings);
        }
        catch (Exception exception)
        {
            if (startupChanged)
            {
                try
                {
                    StartupRegistration.SetEnabled(_settings.StartWithWindows);
                }
                catch (Exception rollbackException)
                {
                    TrayLog.Error("Failed to roll back the startup preference.", rollbackException);
                }
            }

            TrayLog.Error("Failed to save tray settings.", exception);
            ShowError(UiText.Current.SettingsSaveFailed, exception);
            return false;
        }

        _settings = settings;
        UiText.Use(settings.Language);
        _popup.ApplyLocalization();
        ApplyVisualState();
        return true;
    }

    private Task SetModeFromPanelAsync(WakeMode mode)
    {
        if (mode == WakeMode.Inactive)
        {
            return StopAwakeAsync();
        }

        var stopAtUtc = _desiredMode == WakeMode.Inactive ? null : _stopAtUtc;
        return SetModeAsync(mode, stopAtUtc);
    }

    private async void HeartbeatTimerTick(object? sender, EventArgs eventArgs)
    {
        await RefreshOrRenewAsync(showError: false);
    }

    private async Task SetModeAsync(WakeMode mode, DateTimeOffset? stopAtUtc)
    {
        await RunSerializedAsync(async () =>
        {
            var response = await ChangeModeAsync(mode, stopAtUtc);
            ApplyVisualState(response);
        }, showError: true, skipIfBusy: false);
    }

    private async Task SetDurationFromPanelAsync(TimeSpan? duration)
    {
        if (_desiredMode == WakeMode.Inactive)
        {
            return;
        }

        await RunSerializedAsync(async () =>
        {
            DateTimeOffset? stopAt = duration is { } value
                ? DateTimeOffset.UtcNow.Add(value)
                : null;
            var response = await ChangeModeAsync(_desiredMode, stopAt);
            _selectedDuration = duration;
            ApplyVisualState(response);
        }, showError: true, skipIfBusy: false);
    }

    private async Task StopAwakeAsync()
    {
        await RunSerializedAsync(async () =>
        {
            var response = await ChangeModeAsync(WakeMode.Inactive, stopAtUtc: null);
            _selectedDuration = null;
            ApplyVisualState(response);
        }, showError: true, skipIfBusy: false);
    }

    private Task LockAsync()
    {
        try
        {
            SessionActions.Lock();
        }
        catch (Exception exception)
        {
            TrayLog.Error("The workstation could not be locked.", exception);
            ShowError(UiText.Current.LockFailed, exception);
        }

        return Task.CompletedTask;
    }

    private Task StartScreenSaverAsync()
    {
        try
        {
            SessionActions.StartScreenSaver();
        }
        catch (Exception exception)
        {
            TrayLog.Error("The screen saver could not be started.", exception);
            ShowError(UiText.Current.ScreenSaverFailed, exception);
        }

        return Task.CompletedTask;
    }

    private async Task RefreshOrRenewAsync(bool showError)
    {
        if (_stopAtUtc is { } stopAt && stopAt <= DateTimeOffset.UtcNow)
        {
            _desiredMode = WakeMode.Inactive;
            _stopAtUtc = null;
            _selectedDuration = null;
            UpdateHeartbeatTimer();
        }

        await RunSerializedAsync(async () =>
        {
            SetDisplayRequestActive(_desiredMode == WakeMode.KeepAwakeAndDisplayOn);
            var response = _desiredMode == WakeMode.Inactive
                ? await _client.QueryStatusAsync()
                : await _client.UpsertLeaseAsync(_desiredMode, _stopAtUtc);
            EnsureSuccess(response);
            _serviceConnected = true;
            UpdateHeartbeatTimer();
            ApplyVisualState(response);
        }, showError, skipIfBusy: true);
    }

    private async Task RunSerializedAsync(Func<Task> action, bool showError, bool skipIfBusy)
    {
        if (skipIfBusy && !await _requestLock.WaitAsync(0))
        {
            return;
        }
        if (!skipIfBusy)
        {
            await _requestLock.WaitAsync();
        }

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _serviceConnected = false;
            ApplyVisualState();
            TrayLog.Error("A tray operation failed.", exception);
            if (showError)
            {
                ShowError(UiText.Current.OperationFailed, exception);
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task ExitAsync(bool confirmOnFailure)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _popup.Hide();
        _settingsForm?.Hide();
        _heartbeatTimer.Stop();
        await _requestLock.WaitAsync();
        try
        {
            if (_desiredMode != WakeMode.Inactive)
            {
                var response = await _client.ReleaseLeaseAsync();
                EnsureSuccess(response);
                SetDisplayRequestActive(active: false);
            }
        }
        catch (Exception exception)
        {
            TrayLog.Error("Lease release during exit failed.", exception);
            if (confirmOnFailure)
            {
                var answer = MessageBox.Show(
                    UiText.Current.ReleaseFailedQuestion,
                    "WakeGuard",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    _exiting = false;
                    _heartbeatTimer.Start();
                    return;
                }
            }
        }
        finally
        {
            _requestLock.Release();
        }

        _notifyIcon.Visible = false;
        ExitThread();
    }

    private async Task<ServiceResponse> ChangeModeAsync(
        WakeMode requestedMode,
        DateTimeOffset? stopAtUtc)
    {
        var previousMode = _desiredMode;
        var previousStopAtUtc = _stopAtUtc;
        var serviceChanged = false;

        try
        {
            SetDisplayRequestActive(requestedMode == WakeMode.KeepAwakeAndDisplayOn);
            var response = requestedMode == WakeMode.Inactive
                ? await _client.ReleaseLeaseAsync()
                : await _client.UpsertLeaseAsync(requestedMode, stopAtUtc);
            EnsureSuccess(response);
            serviceChanged = true;

            _desiredMode = requestedMode;
            _stopAtUtc = requestedMode == WakeMode.Inactive ? null : stopAtUtc;
            _serviceConnected = true;
            UpdateHeartbeatTimer();
            return response;
        }
        catch
        {
            try
            {
                SetDisplayRequestActive(previousMode == WakeMode.KeepAwakeAndDisplayOn);
                if (serviceChanged)
                {
                    var rollback = previousMode == WakeMode.Inactive
                        ? await _client.ReleaseLeaseAsync()
                        : await _client.UpsertLeaseAsync(previousMode, previousStopAtUtc);
                    EnsureSuccess(rollback);
                }
            }
            catch (Exception rollbackException)
            {
                TrayLog.Error("Failed to roll back a partially applied mode change.", rollbackException);
            }

            throw;
        }
    }

    private void SetDisplayRequestActive(bool active)
    {
        if (active)
        {
            _displayRequest ??= new WindowsDisplayRequest();
            _displayRequest.SetActive(true);
            return;
        }

        _displayRequest?.SetActive(false);
    }

    private void UpdateHeartbeatTimer()
    {
        _heartbeatTimer.Enabled = !_exiting && _desiredMode != WakeMode.Inactive;
    }

    private void ApplyVisualState(ServiceResponse? response = null)
    {
        TrayIconFactory.IconState iconState;
        if (!_serviceConnected)
        {
            _statusText = UiText.Current.StatusDisconnected;
            iconState = TrayIconFactory.IconState.Disconnected;
        }
        else if (_desiredMode == WakeMode.KeepAwakeAndDisplayOn)
        {
            _statusText = UiText.Current.StatusDisplayOn;
            iconState = TrayIconFactory.IconState.DisplayOn;
        }
        else if (_desiredMode == WakeMode.KeepAwake)
        {
            _statusText = UiText.Current.StatusKeepAwake;
            iconState = TrayIconFactory.IconState.KeepAwake;
        }
        else if (response?.EffectiveMode is WakeMode.KeepAwake or WakeMode.KeepAwakeAndDisplayOn)
        {
            _statusText = UiText.Current.StatusOtherUser;
            iconState = TrayIconFactory.IconState.Inactive;
        }
        else
        {
            _statusText = UiText.Current.StatusInactive;
            iconState = TrayIconFactory.IconState.Inactive;
        }

        _notifyIcon.Text = TruncateTooltip($"WakeGuard - {_statusText}");
        SetIcon(iconState);
        _popup.UpdateState(CreatePopupState());
    }

    private TrayPopupForm.State CreatePopupState() =>
        new(
            _statusText,
            _desiredMode,
            _stopAtUtc,
            _selectedDuration,
            _serviceConnected);

    private void SetIcon(TrayIconFactory.IconState state)
    {
        if (state == _currentIconState)
        {
            return;
        }

        var newIcon = TrayIconFactory.Create(state);
        _notifyIcon.Icon = newIcon;
        _currentIcon.Dispose();
        _currentIcon = newIcon;
        _currentIconState = state;
    }

    private static void EnsureSuccess(ServiceResponse response)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException(
                response.ErrorMessage ?? response.ErrorCode ?? UiText.Current.ServiceFailure);
        }
    }

    private static string TruncateTooltip(string text) => text.Length <= 63 ? text : text[..63];

    private static void ShowError(string context, Exception exception)
    {
        MessageBox.Show(
            $"{context}\n\n{exception.Message}",
            "WakeGuard",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _heartbeatTimer.Dispose();
            _popup.Dispose();
            _settingsForm?.Dispose();
            _shutdownWindow.Dispose();
            _displayRequest?.Dispose();
            _notifyIcon.Dispose();
            _currentIcon.Dispose();
            _requestLock.Dispose();
        }

        base.Dispose(disposing);
    }
}
