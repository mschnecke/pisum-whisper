## Why

Two gaps remain between a working app and one that behaves like an installed desktop utility. The
pipeline currently fails silently — a 401, a missing microphone or an exhausted quota produces
nothing the user can see, because the window is usually hidden. And a dictation tool that has to be
launched by hand every morning will not be used.

## What Changes

- Add `INotificationService`, over a notification this application **draws itself** as an Avalonia
  window — one implementation for both platforms, no package, no AUMID. *Corrected 2026-09-01, after
  the spikes:* `CommunityToolkit.WinUI.Notifications` ships its desktop half only for a `-windows`
  TFM this project has ruled out, so `net10.0` can compose a toast but never show one. `osascript`
  goes with it: a second implementation, and a `Process.Start` on the hotkey thread. See `design.md`.
- Port the reference's policy: **errors are forced and ignore the user's preference**, status
  messages respect the `showTrayNotifications` toggle. Someone who silences chatter still needs to be
  told their API key is rejected.
- Wire the five call sites `add-dictation-pipeline` left as log lines. `DictationFailure.Describe`
  already produces all seven titles, by type and `ErrorCategory`, so no mapping is added here.
- Add `IAutostartService`: on Windows a value under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; on macOS a LaunchAgent plist at
  `~/Library/LaunchAgents/net.pisum.whisper.plist`.
- ~~Add `IShellService` to open the log folder.~~ *Delivered by `add-settings-window` as
  `ISystemShell`; nothing to do here.*
- Add the first-launch flow on `add-settings-store`'s detection: honour `startWithSystem`, show a
  welcome notification, and open the settings window, so a new user is pointed at the API key field
  rather than a silent tray icon.

Reference: `tray.rs` for notifications, `tauri-plugin-autostart` for startup, and the `setup()` block
of `lib.rs` for the first-launch sequence.

## Capabilities

### New Capabilities
- `notifications`: the user is informed of errors and significant status changes through the operating system's notification service.
- `autostart`: the application can register itself to launch at login and be unregistered again.

### Modified Capabilities
- `settings-window`: the window opens itself on a first launch. *Added 2026-09-01; originally none.*

## Impact

Depends on `add-settings-store`, `add-dictation-pipeline` and `add-settings-window`. *Corrected
2026-09-01:* it places **no** requirement on `add-packaging-ci` — the AUMID prerequisite was true
only of the toast package above, and removing it was a reason for the decision.

## Non-goals

- No in-app notification centre or history.
- No notification actions, buttons or transcript previews in the toast.
- No `SMAppService` on macOS 13+; the LaunchAgent plist matches the reference and works further back.
