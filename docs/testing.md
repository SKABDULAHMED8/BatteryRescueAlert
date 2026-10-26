# Testing

## 1. What has actually been run

| Check | Result | Where |
|---|---|---|
| Core test baseline before patch | **121 passed, 0 failed** | Previously executed in the original test environment. Version 1.0.5 updates the themed sound selector and notification sound-routing behavior; new patch changes were not executed in the packaging environment. Run `dotnet test tests\BatteryRescueAlert.Core.Tests` on Windows. |
| Previous .NET 8 Windows build | User reported a successful build with 0 warnings and 0 errors on Windows 10 for an earlier revision. | User's Windows 10 PC |
| Version 1.0.5 build and tests | **Not run in the packaging environment** because no .NET SDK is installed there. Run the commands below on Windows. | Pending |
| XAML and source structure | XAML/project XML parses; maximized behavior and sound playback need visual and audio confirmation on Windows 10. | Static validation only |
| Colour contrast | Computed for 20 foreground/background pairs in both themes: all meet their WCAG AA targets | Script check |
| Win32 constants, GUIDs, struct layout | Checked against Microsoft Learn (`SYSTEM_POWER_STATUS`, `WM_POWERBROADCAST`, `RegisterPowerSettingNotification`, Power Setting GUIDs) | Documentation |
| Installer build, signing, resource usage, real battery behaviour | **Not done** | — |

## 2. Run the automated tests (Windows, Linux or macOS)
```powershell
dotnet test tests\BatteryRescueAlert.Core.Tests
```
Coverage map (spec section 31): below/at/under threshold · 31→29 · 29→28 no duplicate · charging · discharging · AC connected/disconnected ·
startup below threshold (with and without saved state) · threshold changes · notifications disabled · invalid settings (value, order, corrupt
file, nulls, unwritable folder) · multiple threshold crossings · unavailable data · notifier failure/exception · pause/resume · view-model validation.

## 3. Manual test checklist (do this on a real laptop)
Tick each item. Expected results in brackets.

**Install & start**
- [ ] `Build-and-Run.cmd` builds and starts the app (window opens, tray icon appears).
- [ ] Start a second copy (window comes forward, no second tray icon).
- [ ] Close the window with ✕ (it hides; a one-time “still running” notification appears; tray icon stays).

**Battery state** (plug / unplug the charger while watching the Dashboard)
- [ ] Percentage matches Windows' battery flyout (± Windows rounding).
- [ ] On battery → “On battery”; plugged in & charging → “Charging” and the “alerts are paused” text; 100 % on AC → “Fully charged”.
- [ ] Update appears within about a second of plugging/unplugging.
- [ ] Sleep and wake the laptop: the display refreshes after resume.
- [ ] Desktop PC / VM: shows “No battery detected”, no crash.

**Alerts** (to avoid waiting hours, temporarily set thresholds just below the current level, e.g. current 62 % → 60/58/56, then Save)
- [ ] Unplug; as the battery crosses each threshold you get exactly one notification with the specified text.
- [ ] No repeated notification while it stays below.
- [ ] Plug in, charge 5+ points above the threshold, unplug, drain: the alert fires again.
- [ ] Exit and restart while below a threshold: no repeat notification.
- [ ] Pause Alerts (tray or Dashboard), cross a threshold: nothing; Resume: one alert if still below.
- [ ] Turn off “Enable battery alerts” and “Show notifications”: nothing shown; dashboard notice explains why.

**Notifications**
- [ ] Test Notification (Settings, Dashboard, tray) opens the sample popup in the lower-right corner.
- [ ] Keep the main window hidden in the tray and press Test Notification: the same popup appears.
- [ ] Confirm a real threshold alert uses the same popup design and message path.
- [ ] With notifications disabled in the app, *Test Notification* explains instead of showing.
- [ ] Close the popup with × and confirm it closes automatically after about eight seconds.
- [ ] Preview the default Windows Notification sound and each listed system tone. Confirm the custom popup and sound play together when Test Notification is pressed.
- [ ] Import an MP3 or WAV from another folder; confirm the app stores a copy under `%APPDATA%\BatteryRescueAlert\CustomTones` and can preview it.
- [ ] Rename or delete the original source file outside the app; confirm the imported copy still plays.
- [ ] Save the custom tone selection, exit and restart; confirm the selection persists and is used by real battery alerts.
- [ ] Delete the selected custom tone in Settings. Confirm the selection returns to Windows notification (default); delete all custom tones and verify the app still uses the default.
- [ ] Try a custom audio file that Windows cannot decode; confirm the visible popup still appears and the sound falls back to the Windows notification tone.

**Settings**
- [ ] Invalid values (empty, `0`, `100`, `abc`, Critical ≥ Warning 2) show an inline message with ⚠ and disable Save.
- [ ] Save, close the app, reopen: values persist. Revert discards edits.
- [ ] Corrupt `settings.json` (type garbage): app starts with defaults and shows a notice.
- [ ] Start with Windows: tick, Save, sign out/in: the app starts hidden in the tray. Untick: it no longer starts. Check `HKCU\...\Run`.

**Window responsiveness**
- [ ] Maximize and confirm no desktop strip remains at any edge; restore the window and confirm it stays in the usable area.
- [ ] Turn taskbar auto-hide on and off while the window is maximized; confirm Windows recalculates the maximized bounds and the window remains responsive.
- [ ] Change screen resolution/display scale, then maximize, restore and re-maximize; confirm system buttons remain visible.

**Accessibility**
- [ ] Complete every task with the keyboard only (Tab / Shift+Tab / Space / Enter / arrow keys in the tabs); the focus outline is always visible.
- [ ] Narrator reads the percentage, headline, toggles and field names.
- [ ] Windows dark mode and light mode both look right (switch while the app is running).
- [ ] 150 % and 200 % display scaling: nothing is clipped; high-contrast theme remains usable.
- [ ] Status is understandable without colour (text + symbol on every state).

**Uninstall**
- [ ] Installer/uninstaller removes the app and the Run entry; settings are kept unless you choose to remove them.

## 4. If the first Windows build fails
The Windows/App code was never compiled against real WPF. Typical fixes: a missing `using`, an ambiguous type between WPF and WinForms
(both are referenced; the code uses explicit usings and avoids `Application`/`MessageBox` ambiguity), or a XAML attribute the compiler
rejects. Send the exact error text with the file/line and it is normally a one-line fix. `dotnet build` prints it.

## 5. Performance (measure, do not guess)
1. Publish (`scripts\publish.ps1`) and start the self-contained exe; leave it in the tray, on battery power.
2. `powershell -ExecutionPolicy Bypass -File scripts\measure-resources.ps1 -Seconds 600`
3. Record average / maximum CPU %, working set and private memory, handles, threads (saved to `artifacts\resource-measurements.csv`).
4. Repeat with the window open on the Dashboard, and across a few plug/unplug events.
5. Cold start time: note the seconds from double-click to tray icon.

Practical targets to judge the result against: ~0 % CPU when idle (single brief wakeups on power events and every 5 minutes), no network
traffic (check Resource Monitor → Network), no growth in memory or handles over an hour. Fill in the real numbers here:

| Measurement | Result |
|---|---|
| Idle CPU (avg / max) | _not measured_ |
| Working set (avg / max) | _not measured_ |
| Private memory | _not measured_ |
| Cold start | _not measured_ |
| Network connections | _not measured_ |

## 6. Known limitations of the test setup
The sandbox that produced the first version had no Windows machine, so battery hardware behaviour, notification delivery, tray behaviour,
and the visual layout are unverified. Everything marked “Not done” above should be considered open until ticked in section 3.
