# Licenses, permissions and commands

## 1. Choosing a license for this project
The project ships under the **MIT License** (`LICENSE`): very permissive, short, compatible with everything it uses. It is the default
because the app is a small utility and the dependencies (.NET, WPF) are also MIT. Alternatives, if your goals differ:

| License | Choose it if you want… |
|---|---|
| **MIT** (current) | Anyone may use, modify and sell it with attribution; no patent clause. |
| Apache-2.0 | Same freedom plus an explicit patent grant and a NOTICE mechanism. |
| GPL-3.0 | Derived works must stay open source (copyleft). |
| Proprietary / EULA | You keep it closed. You may still use the MIT-licensed .NET/WPF runtime; keep the Microsoft notices in `THIRD-PARTY-NOTICES.md`. |

To change it: replace `LICENSE`, update the About text in `MainWindow.xaml`, `THIRD-PARTY-NOTICES.md`, the README and the installer
(`LicenseFile`). Replace the copyright holder line with your own name or organisation before publishing. This is not legal advice.

Third-party components and their licenses: `THIRD-PARTY-NOTICES.md`. Segoe UI fonts are used from the system and are not redistributed.

## 2. Permissions the app needs
| Area | Needed? | Details |
|---|---|---|
| Administrator rights | **No** | Manifest requests `asInvoker`. The per-user installer also needs none. |
| Registry | Write **HKCU** `...\Run` only if *Start with Windows* is on | Removed when turned off or uninstalled. |
| File system | Read/write `%APPDATA%\BatteryRescueAlert\` | Plus `%LOCALAPPDATA%\Programs\...` if you use the user installer. |
| Network / firewall | **None** | No prompt will appear. |
| Notifications | App-owned WPF on-screen popups | These popups are drawn by the running app. They are not Windows Notification Center toasts and do not use the per-app toast setting. |
| Hardware / sensors | Battery status via standard Windows power APIs | No special capability. |
| Microsoft Store capabilities | Not applicable | Not packaged for the Store in v1. |

## 3. Code signing (recommended before sharing)
Unsigned executables trigger Windows SmartScreen warnings. To sign you need a code-signing certificate from a certificate authority
(or a managed signing service). Then:
```powershell
$env:BRA_SIGN_PASSWORD = '<your pfx password>'          # session only; never commit it
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Sign C:\path\to\certificate.pfx
```
This signs the exe and the installer with SHA-256 and a timestamp (`signtool` ships with the Windows SDK). Reputation with SmartScreen
builds over time even for signed apps. Publish the SHA-256 hash printed at the end next to your download.

## 4. Command cheat sheet
```powershell
winget install Microsoft.DotNet.SDK.8                 # build tools (once)
winget install Microsoft.DotNet.DesktopRuntime.8      # only to run the framework-dependent build
winget install JRSoftware.InnoSetup                   # only to build the installer

dotnet build BatteryRescueAlert.sln -c Release        # build everything
dotnet test  tests\BatteryRescueAlert.Core.Tests      # run tests
dotnet run   --project src\BatteryRescueAlert.App     # run from source (Start-with-Windows is disabled in this mode by design)

scripts\publish.ps1                                   # artifacts\publish\self-contained|framework-dependent
scripts\build-installer.ps1 [-Sign cert.pfx]          # artifacts\installer\BatteryRescueAlert-Setup-<version>.exe
scripts\install-user.ps1 -StartWithWindows            # installer-free install for this user
scripts\install-user.ps1 -Uninstall [-RemoveSettings] # remove it
scripts\measure-resources.ps1 -Seconds 300            # measure CPU/RAM of the running app

# Silent installer switches (Inno Setup)
BatteryRescueAlert-Setup-1.0.5.exe /VERYSILENT /SUPPRESSMSGBOXES /TASKS="startup"
"%LOCALAPPDATA%\Programs\Battery Rescue Alert\unins000.exe" /VERYSILENT

# Manual cleanup of the "start with Windows" entry
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v BatteryRescueAlert /f
```
Run `powershell -ExecutionPolicy Bypass -File ...` per command as shown, or `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once.

## 5. Branding and icons
`assets/icons/BatteryRescueAlert.ico` contains 16, 24, 32, 48, 64, 128 and 256 px frames (taskbar, tray, Start menu, installer).
Small sizes use a simplified drawing. Source of truth: `scripts/make-icons.py` (shield + battery + warning waves; original artwork).
Brand colours: navy `#0E3B4C`, teal `#148280`, amber `#FFB020`.
