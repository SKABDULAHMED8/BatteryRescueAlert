# Architecture

## Technology choice

| Option | Verdict |
|---|---|
| **C# / .NET 8 / WPF** | **Chosen.** Mature, native Windows desktop UI, easy access to Win32 power messages, tiny dependency footprint (none beyond the framework), simple packaging (single-file publish), long-term support. |
| WinUI 3 | Modern look, but needs the Windows App SDK, heavier packaging and more moving parts for a tiny utility. |
| Win32 / C++ | Smallest memory, but slower to build, harder to keep accessible and maintainable. |
| Python | Needs an interpreter bundle, larger and slower to start; no strong reason here. |
| Windows service | Rejected: a service cannot show UI, tray icons or user notifications in the signed-in session. |

WinForms' `NotifyIcon` is used only for the system tray icon and menu. Visible battery alerts use an app-owned WPF popup window. This avoids treating a legacy tray-balloon request as proof that a Windows toast was displayed and adds no package dependency.
**Trade-off:** these are on-screen popups, not Windows Notification Center toasts; the app must be running in the signed-in desktop session.

## Layers

```
BatteryRescueAlert.App       (WPF, net8.0-windows)   XAML views, themes, AppHost (composition root)
        │
        ├── BatteryRescueAlert.Windows (net8.0-windows)  PowerEventWindow, WindowsBatteryMonitor, TrayIconService,
        │                                                WindowsNotificationService, StartupRegistration, SystemTheme
        │
        └── BatteryRescueAlert.Core    (net8.0, no UI/OS types)  ThresholdEngine, AlertCycleState, AlertCoordinator,
                                                                  DashboardPresenter, MainViewModel, settings, logging
```
Everything with decisions in it lives in **Core**, so it is unit-tested on any OS. The Windows and App layers are thin adapters.
(The planned `Windows.Tests` project was not created: those classes are P/Invoke and UI glue that can only be verified on a real machine — see `testing.md`.)

## Battery event flow

```
 Windows power broadcast                         (no polling loop)
   WM_POWERBROADCAST ─────────────┐
   PBT_POWERSETTINGCHANGE         │   hidden top-level window (PowerEventWindow)
   PBT_APMPOWERSTATUSCHANGE       │   registered for GUID_BATTERY_PERCENTAGE_REMAINING
   PBT_APMRESUME*                 │   and GUID_ACDC_POWER_SOURCE
                                  ▼
                   300 ms debounce (bursts → one read)        slow safety refresh (5 min) ─┐
                                  ▼                                                       │
                   GetSystemPowerStatus → PowerStatusMapper → BatteryState  ◄─────────────┘
                                  ▼  (only if changed)
                           AlertCoordinator
                  ┌───────────────┼───────────────────┐
                  ▼               ▼                   ▼
            ThresholdEngine   Notification path    Snapshot → DashboardPresenter → MainViewModel → WPF + tray text
                  │          (setting on? de-dup? failure handled?)
                  ▼
        alert state saved to state.json (only when it changed)
```
* **Why a hidden top-level window, not a message-only window?** `PBT_APMPOWERSTATUSCHANGE` and the resume events are *broadcast*
  messages, and broadcasts reach top-level windows only. The window has no size, is never shown and has no taskbar button.
* **Why read `GetSystemPowerStatus` instead of parsing the notification payload?** One code path for startup, resume, events and the
  safety refresh; the payload only tells us *that* something changed. For several batteries Windows reports one system-wide value
  here; the app never computes its own aggregate.
* **Safety refresh:** Windows drivers can miss events. One cheap read every 5 minutes covers that. It is a timer tick, not a loop,
  and does nothing when the state is unchanged. Remove it (`WindowsBatteryMonitor`) if you prefer a purely event-driven design.

## Threshold state machine

State per threshold: **armed** or **fired**. Rules (implemented in `ThresholdEngine`, 30+ tests):

```
              percent <= threshold  AND enabled AND on battery AND not paused AND alerts on
   ┌───────┐ ─────────────────────────────────────────────────────────────────────────────▶ ┌───────┐
   │ ARMED │                                                                                │ FIRED │  → one notification
   └───────┘ ◀───────────────────────────────────────────────────────────────────────────── └───────┘    (most severe only)
              percent >= threshold + 5   (hysteresis, any power state)
```
| Situation | Result |
|---|---|
| 31 → 29, threshold 30 | Fires. No exact value needed. |
| 29 → 28 | Nothing (already fired). |
| 40 → 18 | One alert for the most severe crossed threshold; all crossed thresholds marked fired. |
| Plugged in / charging | Nothing consumed. Unplugging below a threshold warns once. |
| Paused / alerts off | Nothing consumed. Resuming below a threshold warns once. |
| Charged to threshold + 5 | That threshold re-arms. |
| App restart | Fired set restored from `state.json`; re-armed immediately if the battery has recovered. |
| User edits thresholds | Thresholds the battery is already below are marked fired silently (no surprise alert). |
| No / unknown battery data | Ignored; never treated as 0 %; state untouched. |
| Invalid settings | No alerts; the UI blocks saving them and invalid files fall back to defaults. |

## Notification system
`AlertCoordinator` → (setting off? → recorded, not shown) → (60 s de-duplication per threshold) → `INotificationService` → `NotificationPopupWindow`. The service opens a WPF popup in the lower-right corner of the usable desktop area. The Test Notification button and actual battery-threshold alerts call this exact same service. Success is returned only after the popup window is open and visible; failures are logged and returned to the UI. Message text lives in `AlertMessages` and is covered by tests. These popups are not recorded in Windows Notification Center and do not depend on Focus Assist or per-app toast registration.

## Startup behaviour
`HKCU\...\Run\BatteryRescueAlert = "<exe>" --background`. With `--background` the window stays hidden and only the tray icon appears
(if the tray icon cannot be created the window is shown so the app is never invisible). The registry is the source of truth for the
checkbox; if Windows disabled the entry (Task Manager / Settings) the Settings page says so. The app refuses to register
`dotnet.exe` or a non-`.exe` path.

## Configuration storage
JSON in `%APPDATA%\BatteryRescueAlert\` written atomically (temp file + replace). Corrupt, incomplete or contradictory files are
repaired to safe defaults with a visible notice; invalid settings are never written.

## Error handling
No unhandled path crashes the app: battery read failure → "unavailable" state; notifier exception → contained; UI handler exception →
contained; unhandled UI exception → logged (type only) and the app keeps running; logging and persistence never throw.

## Multiple batteries
`GetSystemPowerStatus` reports a single system-wide percentage and flags. The app uses exactly that value. It does not read individual
batteries and does not invent an aggregate.

## Logging
Optional (Settings), off by default. Lines like *Application started · Battery status · Threshold crossed · On-screen notification displayed ·
Notification failure · Application stopped*. Exceptions are logged by **type and HRESULT only** (messages and stack traces can contain
user paths). The file rolls over at 256 KB to a single `.1` backup.

## Key decisions (summary)
Defaults: thresholds 30/25/20; percent range 1–99 and strictly decreasing (enforced even for disabled rows); re-arm margin 5;
de-duplication window 60 s; debounce 300 ms; safety refresh 5 min; *Start with Windows* off until the user turns it on.
