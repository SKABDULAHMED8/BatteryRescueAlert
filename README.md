# Battery Rescue Alert

> **Get an early warning before your laptop battery becomes critical.**

Battery Rescue Alert is a small, lightweight Windows utility that shows a clear notification when Windows reports that your
battery is at or below thresholds you choose (defaults: **30 %**, **25 %**, **20 %**). It only warns you. It never closes programs,
changes power settings, or shuts anything down.

<p align="center"><img src="assets/icons/app-logo-512.png" alt="Battery Rescue Alert icon" width="160"></p>

## Status — read this first

| Part | State |
|---|---|
| Alert logic, settings, view-model (`BatteryRescueAlert.Core`) | Core behavior previously passed 121 baseline tests. Version 1.0.5 fixes the selected-tone ComboBox contrast, separates tray-hint sound from battery-alert sound, and adds explicit custom-tone backup/removal confirmations. The current patch has not been built in this packaging environment; run the full suite on Windows with `dotnet test tests\BatteryRescueAlert.Core.Tests`. |
| Windows integration (power events, tray, notifications, startup and sound playback) | The earlier popup path was visibly tested by the user on Windows 10. Version 1.0.5 adds a fully themed sound selector, independent tray-hint sound selection, and custom-tone backup/removal confirmations; it has not been compiled or run in this packaging environment. |
| WPF interface and window sizing | Version 1.0.5 removes maximum-size limits that interfered with native maximize behavior and responds to display/desktop setting changes. Verify maximize, restore, and taskbar auto-hide on Windows. |
| Installer / signing | Scripts are provided; the installer has **not** been built and the app is **not** code-signed. |
| Resource usage | **Not measured.** A measuring script is included. No numbers are claimed. |

Rebuild this patch on Windows and work through the checklist in `docs/testing.md` before relying on it or sharing it.

---

## Quick start (Windows 10 / 11, 64-bit)

### Option A — one click (build from source)
1. Install the .NET 8 SDK (once). In a terminal: `winget install Microsoft.DotNet.SDK.8`
2. Double-click **`Build-and-Run.cmd`** in this folder. It builds the app and starts it.
3. Look for the battery-shield icon in the notification area (click `^` if hidden). Click it to open the window.

### Option B — build, test and publish with commands
```powershell
# in the project folder (PowerShell)
winget install Microsoft.DotNet.SDK.8              # once
powershell -ExecutionPolicy Bypass -File scripts\build.ps1      # restore + build + run tests
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1    # creates artifacts\publish\...
```
Result: `artifacts\publish\self-contained\BatteryRescueAlert.exe` runs on any Windows 10/11 x64 PC with nothing installed.
`artifacts\publish\framework-dependent\BatteryRescueAlert.exe` is much smaller but needs the runtime:
`winget install Microsoft.DotNet.DesktopRuntime.8`.

### Option C — install for yourself without an installer
```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-user.ps1 -StartWithWindows
```
Copies the app to `%LOCALAPPDATA%\Programs\Battery Rescue Alert`, adds a Start Menu shortcut, and (optionally) starts it with Windows.
Remove it with `scripts\install-user.ps1 -Uninstall` (add `-RemoveSettings` to delete your settings too).

### Option D — real installer
```powershell
winget install JRSoftware.InnoSetup
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```
Produces `artifacts\installer\BatteryRescueAlert-Setup-1.0.5.exe` (per-user, no administrator rights, with uninstaller).
To sign it, see `docs/LICENSES-AND-PERMISSIONS.md`.

> **"Windows protected your PC" (SmartScreen)** will appear for an unsigned build. Choose *More info → Run anyway* only for a build you made yourself.

---

## Using the app

* **Dashboard** — shows the battery percentage, a level bar, the power state (on battery / charging / fully charged), a plain-language
  headline and what to do. Below it are your thresholds and two buttons: **Test Notification** and **Pause alerts**.
* **Settings** — enable/disable alerts, turn individual thresholds on or off, change the percentages, start with Windows,
  show notifications, select a Windows system sound, import/preview/delete custom tones, and enable the optional diagnostic log. Press **Save changes** (or **Revert**) to apply a new sound selection and other draft settings. Invalid threshold combinations are blocked with a message.
* **Tray icon** — hover for the percentage. Left-click opens the window. Right-click for *Open · Settings · Test Notification · Pause Alerts · Exit*.
* Closing the window **hides** it to the tray. Use *Exit* in the tray menu to quit.
* Starting a second copy simply brings the first one forward.

### Test notification
Settings → **Test Notification** (also on the Dashboard and in the tray menu) opens an app-owned popup in the lower-right corner. The popup is created by Battery Rescue Alert itself, not by Windows Notification Center. Real battery warnings use the same popup path. A successful test means the popup window opened; it also requests playback of the selected alert tone. The default tone uses the current Windows `Notification` event sound. Other system event tones can be selected when they have an available WAV file. A successful popup result does not prove that speakers are unmuted or that the selected audio codec can be decoded.

### How alerts behave
* An alert fires when Windows reports the battery **at or below** a threshold — it never needs the exact number (31 % → 29 % still fires the 30 % alert).
* Each threshold alerts **once per discharge**. If the battery drops past several at once, you get **one** notification for the most severe.
* While plugged in or charging, alerts are paused (and the UI says so). Unplugging while already below a threshold gives one warning.
* A threshold re-arms after the battery recovers **5 points above it** (for example 35 % for the 30 % alert).
* Editing a threshold never causes a surprise alert for a level you are already below.

### Configuration & data
| What | Where |
|---|---|
| Settings | `%APPDATA%\BatteryRescueAlert\settings.json` |
| Which alerts already fired | `%APPDATA%\BatteryRescueAlert\state.json` |
| Optional diagnostic log (off by default, ≤ ~0.5 MB) | `%APPDATA%\BatteryRescueAlert\diagnostic.log` |
| Imported custom tone copies | `%APPDATA%\BatteryRescueAlert\CustomTones` |
| Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `BatteryRescueAlert` |

---

## Features
Configurable thresholds with validation · event-driven monitoring (no polling loop) · tray icon and menu · on-screen battery alert popups ·
light and dark theme that follow Windows · keyboard accessible with visible focus · single instance · optional start with Windows ·
selectable Windows notification tones · imported custom alert tones · optional rolling diagnostic log · works fully offline.

## Supported Windows versions
Windows 10 version 1809 or newer, and Windows 11, 64-bit. A laptop or tablet with a battery is needed for alerts; on a desktop PC
the app explains that no battery was detected.

## Troubleshooting
| Problem | What to try |
|---|---|
| No alert popup appears | Use *Test Notification*. The app-owned popup should appear in the lower-right corner. If the test status reports a failure, use that message as the starting point for diagnosis. |
| No tray icon | Click `^` in the taskbar; drag the icon out to pin it. Taskbar settings → *Other system tray icons*. |
| No alert sound | In Settings, preview the selected tone. Check Windows volume/output. A missing custom file or unsupported codec falls back to the Windows notification tone; the app-owned popup can still appear when sound playback fails. |
| “No battery detected” | Windows reports no battery (desktop PC or virtual machine). |
| Percentage looks stuck | Some batteries report coarse steps; Windows controls this. |
| Start with Windows is on but the app does not start | Windows Settings → Apps → Startup (or Task Manager → Startup) may have switched it off; the Settings page tells you. |
| Settings reset to defaults | `settings.json` was unreadable; the app shows a notice and uses safe defaults. Delete the file to clear it. |
| Build error on first compile | See `docs/testing.md` section 4. |

## Privacy
Everything stays on your PC. No account, no internet connection, no telemetry, no battery history, no device identifiers.
Details: `docs/privacy-and-security.md`.

## Security
Runs as a normal user (never asks for administrator rights). Startup registration is current-user only. The app launches no
commands (the only process it starts is the fixed Windows `ms-settings:notifications` page when you click the link).
Settings are validated on load and before saving.

## Limitations (important)
* Battery Rescue Alert depends on battery information reported by Windows and the device hardware.
* Battery percentage may not change at every 1 % interval.
* Battery warning popups are shown by the running app and do not depend on Windows Notification Center or Focus Assist.
* The application cannot prevent a laptop from shutting down if the battery becomes critically depleted.
* The application cannot guarantee that a notification will appear after the operating system has already shut down or suspended the application.
* If Windows reports several batteries, the app uses the single system-wide value Windows reports (it never calculates its own).

## Development
```text
BatteryRescueAlert/
├── BatteryRescueAlert.sln
├── Build-and-Run.cmd
├── src/
│   ├── BatteryRescueAlert.Core/       net8.0           Alert engine, settings, coordinator, view-model (no Windows/WPF types)
│   ├── BatteryRescueAlert.Windows/    net8.0-windows   Power events, battery reads, tray, notifications, startup, theme detection
│   └── BatteryRescueAlert.App/        net8.0-windows   WPF window, themes, composition root (AppHost)
├── tests/BatteryRescueAlert.Core.Tests/                xUnit tests
├── assets/icons/                                      .ico, PNG logo, preview (generated by scripts/make-icons.py)
├── installer/BatteryRescueAlert.iss                   Inno Setup script
├── scripts/                                           build, publish, installer, install-user, measure-resources, make-icons
└── docs/                                              architecture, testing, privacy & security, licenses & permissions
```
Requirements: Windows 10/11, .NET 8 SDK. Visual Studio 2022 or VS Code both work: open `BatteryRescueAlert.sln`.
Regenerate icons with `pip install pillow` then `python scripts/make-icons.py`.

## Testing
`dotnet test tests\BatteryRescueAlert.Core.Tests` (the tests need no Windows APIs). Manual checklist and performance method: `docs/testing.md`.

## License
MIT — see `LICENSE`. Third-party components: `THIRD-PARTY-NOTICES.md`. Other license choices and permissions: `docs/LICENSES-AND-PERMISSIONS.md`.
