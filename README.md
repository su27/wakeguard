<div align="center">
  <img src="src/WakeGuard.Tray/Assets/WakeGuard.png" width="144" alt="WakeGuard icon">
  <h1>WakeGuard</h1>
  <p><strong>Keep your PC awake, even with the screen off or Windows locked.</strong></p>
  <p>Windows 10/11 x64 · English / 简体中文 · MIT licensed</p>
  <p><a href="https://github.com/su27/wakeguard/releases/latest">Download the latest release</a></p>
</div>

WakeGuard lives in your system tray and keeps Windows from sleeping while you
download files, run long tasks, or leave work running unattended. You can let the
screen turn off as usual or keep it on too.

## Features

- **Three modes:** follow Windows settings, keep the PC awake, or keep both the PC and screen awake.
- **Flexible duration:** unlimited, 30 minutes, 1 hour, 2 hours, or 4 hours, with remaining time and end time shown in the panel.
- **Works while locked:** background tasks can continue after you lock Windows or the screen turns off.
- **Quick access:** left-click or right-click the tray icon to open the same compact control panel.
- **Handy shortcuts:** lock the PC or start the screen saver without changing your wake mode or timer.
- **Personal preferences:** switch between English and Simplified Chinese, and choose whether WakeGuard starts when you sign in.


<img width="1803" height="507" alt="image" src="https://github.com/user-attachments/assets/79c2c972-1202-40bd-a0bc-df08b71c2427" />


## Getting started

1. Download `WakeGuard-<version>-win-x64.msi` from [Releases](https://github.com/su27/wakeguard/releases/latest).
2. Run the installer and approve the administrator prompt.
3. Open **WakeGuard** from the Start menu.
4. **Left-click or right-click** its tray icon to open the panel. If the icon is hidden, look in the tray's overflow menu.

The interface requires **Microsoft Edge WebView2 Runtime**, usually already
installed on Windows. If it is missing, the installer tells you where to get it.
You do not need to install .NET separately.

> The installer is not digitally signed, so Windows may show an “Unknown publisher”
> warning. Download it only from this repository's Releases page.

## Using the panel

Choose the mode that suits your task:

| Mode | What it does |
| --- | --- |
| **System default** | Stops keeping the PC awake and lets Windows manage sleep and the display normally. |
| **Keep awake** | Keeps the PC awake while allowing the screen to turn off and Windows to lock as usual. |
| **Keep screen on** | Keeps both the PC and screen awake. |

Then choose a duration. When the timer ends, WakeGuard returns to **System default**;
it does not immediately put the PC to sleep. You can also select **System default**
at any time to stop keeping it awake.

The two buttons at the bottom **lock the PC** or **start the screen saver**.
Neither changes the selected mode or timer. Starting a screen saver does not itself
lock Windows.

At the top right:

- **Settings (gear):** change the language or start-at-sign-in preference, and check the app version.
- **Exit (door and arrow):** stop this instance from keeping the PC awake and quit WakeGuard. This does **not** shut down the PC.

## Important limits

WakeGuard prevents sleep caused by inactivity. It does not override manual Sleep,
Hibernate, Restart, or Shut down, laptop lid actions, critical-battery protection,
or system and administrator policies. It does not change your power plan or
simulate mouse and keyboard activity.

## Updating or uninstalling

To update, run the newer MSI installer. Your language and startup preferences are
preserved. Open WakeGuard again after updating.

To uninstall, remove **WakeGuard** in **Settings → Apps → Installed apps**.

## More information

- [Architecture](docs/architecture.md)
- [Testing and validation](docs/testing.md)
- [MIT License](LICENSE)
