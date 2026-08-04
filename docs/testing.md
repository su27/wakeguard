# Testing checklist

## Automated

```powershell
dotnet build .\WakeGuard.slnx -c Release
dotnet test .\WakeGuard.slnx -c Release --no-build
```

Core tests cover lease renewal, lease expiry, user-selected stop time, multiple-user isolation, strongest-mode selection, downgrade behavior, transactional power API failures, and bounded IPC framing.

## Installation

1. Install the MSI on a machine where WakeGuard has never been installed.
2. Confirm `sc.exe qc WakeGuard` reports `SERVICE_START_NAME` as `NT AUTHORITY\LocalService` and `START_TYPE` as demand start.
3. Confirm `sc.exe qtriggerinfo WakeGuard` reports a named-pipe start trigger for `WakeGuard.Service.v1`.
4. With wake mode inactive, wait 30 seconds and confirm the service reaches `Stopped`.
5. Enable a wake mode and confirm the pipe request automatically starts the service without elevation.
6. Confirm the tray starts from the Start menu without elevation.
7. Reinstall the same MSI and confirm repair succeeds.
8. Install a newer MSI over the old version and confirm the tray closes, the service stops, files update, and the service remains stopped until requested.
9. Uninstall while a wake mode is active and confirm no WakeGuard entries remain in `powercfg /requests`.

## Tray interaction and settings

1. Left-click the tray icon and confirm the awake control panel opens without showing a native menu.
2. Right-click the tray icon and confirm the native menu contains only **Settings** and **Exit**.
3. Disable startup in Settings and confirm the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WakeGuard` value is absent; enable it and confirm it points to the installed tray executable.
4. Switch between Chinese and English and confirm the control panel, tray menu, settings window, status text, and error prompts update without restarting.
5. Close and restart the tray and confirm startup and language preferences persist.
6. Confirm the About section reports the same version as the installed MSI.

## Power behavior

1. Enable **保持唤醒** and verify `WakeGuard.Service.exe` owns `SYSTEM` and `EXECUTION` while `DISPLAY` has no WakeGuard request.
2. Wait for the configured display timeout; verify the display turns off and background work continues.
3. Lock the workstation, wait longer than the configured sleep timeout, unlock, and verify the machine never entered Modern Standby.
4. Enable **保持唤醒 · 屏幕常亮** and verify `WakeGuard.Tray.exe` owns `DISPLAY` while `WakeGuard.Service.exe` owns `SYSTEM` and `EXECUTION`.
5. Test all four timers and confirm the service clears requests at the deadline.
6. End `WakeGuard.Tray.exe` from Task Manager and confirm requests disappear within 75 seconds.
7. Stop and restart the service while the tray is running; confirm the tray reconnects and recreates its lease on the next heartbeat.
8. Disable wake mode, confirm the service stops after its quiet period, then re-enable wake mode and confirm trigger-start works again.
9. Reboot while a mode is active; confirm the machine starts inactive until a tray requests a mode again.

## Session behavior

1. Use **保持唤醒 · 立刻锁屏** and confirm locking occurs only after the service acknowledges the request.
2. Use **保持唤醒 · 播放屏保** with a configured screen saver.
3. Clear the configured screen saver and confirm the built-in black screen saver is used.
4. With Fast User Switching, request different modes from two users and confirm the strongest mode wins.
5. Exit one user's tray and confirm the other user's lease remains active.
