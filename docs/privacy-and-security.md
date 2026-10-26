# Privacy and security

## Privacy statement
Battery Rescue Alert works entirely on your PC.
* **Collected:** nothing. No personal information, no battery history, no device identifiers, no location, no browsing history, no documents or application contents.
* **Network:** none. The app makes no internet connections and needs none. (Verify with Resource Monitor → Network.)
* **Account:** none.
* **Telemetry / crash reporting:** none. If it is ever added it must be a separate, explicit, opt-in decision.
* **Stored locally:** `settings.json` (your thresholds, switches, selected alert tone), `state.json` (which thresholds already alerted), imported custom tone copies in `CustomTones`, and an optional diagnostic log (off by default). Location: `%APPDATA%\BatteryRescueAlert\`. Delete the folder at any time; missing custom tones fall back to the Windows notification sound.
* **Diagnostic log content:** generic events only (for example "Threshold crossed: Warning2 (reported 24%)"). Exceptions are logged by type and HRESULT only.

## What the app never does
Close applications · kill processes · force shutdown or restart · delete files · modify your documents · change Windows power settings ·
disable security features · pretend to prevent a battery shutdown. It only shows warnings.

## Threat model and mitigations
| Risk | Mitigation |
|---|---|
| Privilege abuse | `asInvoker` manifest; no admin rights; no service; no scheduled task; current-user registry key only. |
| Command injection | No shell execution. The only process started is the constant URI `ms-settings:notifications`. |
| Malicious or corrupt settings file | Deserialisation to a fixed type, nulls repaired, values range- and order-validated, fallback to defaults, atomic writes, invalid values never saved. |
| Startup hijack | Registers only its own exe path (quoted), refuses `dotnet.exe` and non-`.exe` paths, writes only `HKCU`. |
| Information leakage via logs | Off by default, no personal data, type-only exception logging, 256 KB cap with one backup. |
| Second-instance abuse | Session-local mutex/event, no data passed between instances. |
| Supply chain | No third-party runtime dependencies. Test-only packages are pinned in the test project. |
| Tampered download | Publish an SHA-256 hash (the installer script prints it) and code-sign releases — see `LICENSES-AND-PERMISSIONS.md`. |

## Reporting a vulnerability
Create a private security report with the maintainer of your fork/repository rather than a public issue. Include the version,
Windows version and steps to reproduce.
