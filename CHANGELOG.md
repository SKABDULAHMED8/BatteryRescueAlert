# Changelog

## 1.0.5 - Sound selector and notification polish
- Replaced the native-themed ComboBox presentation with a fully themed template so the selected tone is readable against the dark surface; selected and highlighted dropdown entries retain readable contrast.
- The "Still running in the notification area" popup uses the Windows notification sound, regardless of the selected custom battery alarm tone. Test Notification and real battery alerts continue to use the selected tone.
- Adding a custom tone now confirms the backup folder, explains that the copied backup survives deletion of the original file, and reminds the user to save changes to activate the new tone.
- Deleting a custom tone now confirms successful removal from the app's backup and explains when the Windows default is selected automatically; the Settings status line gives the same information.
- The source archive still requires Windows/.NET build and runtime verification.

## 1.0.4 - Window responsiveness and configurable alert sounds
- Removed maximum-size constraints from the maximized window. Windows now calculates maximized bounds, including taskbar auto-hide and monitor/DPI changes.
- Re-checks restored window placement after display, DPI or desktop-setting changes without fighting native maximized sizing.
- Added sound selection. The default is the current Windows Notification sound; other active sound-scheme events are listed when their WAV files are available.
- Added custom tone import, preview, and delete. Files are copied into `%APPDATA%\BatteryRescueAlert\CustomTones`, limited to 20 MB, and support WAV, MP3, WMA, M4A, AAC and FLAC extensions. Custom audio plays for up to five seconds per alert.
- A deleted or missing selected custom tone falls back to the Windows notification sound; when no valid custom tone remains, the default is selected.
- Added tests for default sound selection, settings persistence and saving a custom sound selection. Build and tests still require verification on Windows.

## 1.0.3 - Maximized window sizing correction
- Fixed `FitToWorkArea()` so `MaxWidth` and `MaxHeight` allow the window to occupy the complete usable work area when maximized.
- Kept the margin for the normal/restored window size, so it remains centered and visible on smaller screens.
- Corrected the desktop strip and unused gap caused by the earlier maximum-size constraints.

## 1.0.2 - Notification display correction
- Replaced the legacy tray-balloon notification path with an app-owned WPF desktop popup.
- Routed test notifications and real battery-threshold alerts through the same popup service.
- The test status now reports success only after the popup window is open and visible.
- No additional NuGet packages are required.


## 1.0.1 - Reliability and window-layout fixes

- Fixed a threshold-state bug: changing a threshold now clears the prior fired flag for that threshold, so a stale flag cannot suppress the new boundary.
- Added a one-time migration for version 1 alert-state files. Their fired flags are reset because the old format did not identify which threshold values they belonged to. User settings are preserved.
- Restored the documented 5-minute safety refresh; Windows power events remain the primary monitoring path.
- Constrained the main window to the usable desktop work area so the title bar and minimize/maximize/close buttons remain visible on short screens and high display scaling.
- Added four regression tests for changed thresholds, legacy-state migration and settings persistence. The tests need to be run on Windows with `dotnet test tests\BatteryRescueAlert.Core.Tests` before release.

## 1.0.0 (initial release candidate)
- Configurable early-warning battery alerts (defaults 30 / 25 / 20 %), once per discharge, most-severe-only on big drops.
- Event-driven monitoring via Windows power notifications (+ 5-minute safety refresh).
- Dashboard, Settings and About pages; light/dark theme following Windows; keyboard and screen-reader friendly.
- Tray icon and menu; native Windows notifications; Test Notification; Pause/Resume alerts.
- Optional start with Windows (current user only); single instance; optional rolling diagnostic log.
- 121 unit tests for the logic layer. Windows/WPF layer awaiting first verification on a real machine (see docs/testing.md).
