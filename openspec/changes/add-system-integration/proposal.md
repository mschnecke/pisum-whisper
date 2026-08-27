## Why

Two gaps remain between a working app and one that behaves like an installed desktop utility. The
pipeline currently fails silently — a 401, a missing microphone or an exhausted quota produces
nothing the user can see, because the window is usually hidden. And a dictation tool that has to be
launched by hand every morning will not be used.

## What Changes

- Add `INotificationService`:
  - Windows — toast via `CommunityToolkit.WinUI.Notifications`, which requires an AUMID registered by
    a Start-menu shortcut. That constraint is recorded here and satisfied by `add-packaging-ci`.
  - macOS — `osascript -e 'display notification ... with title ...'`, which needs no bundle signing.
    This is exactly what the reference's own installer script already does.
- Port the reference's notification policy precisely, because the distinction matters: **errors are
  forced and ignore the user's preference**, while status messages respect the
  `showTrayNotifications` toggle. Someone who silences chatter still needs to be told their API key
  is rejected.
- Wire the notification call sites left marked in `add-dictation-pipeline`, mapping
  `AppException.Category` to titles: Recording Error, Configuration Error, Network Error,
  Authentication Error, Rate Limit Error, Transcription Error, Output Error.
- Add `IAutostartService`: on Windows a value under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; on macOS a LaunchAgent plist at
  `~/Library/LaunchAgents/net.pisum.whisper.plist`.
- Add `IShellService` to open the log folder (`explorer.exe` / `open`), completing the Logging tab.
- Add the first-launch flow, hanging off the detection added in `add-settings-store`: enable
  autostart when `startWithSystem` is set, show a welcome notification, and open the settings window
  so a new user is pointed at the API key field rather than left with a silent tray icon.

Reference: `tray.rs` for notifications, `tauri-plugin-autostart` for startup, and the `setup()` block
of `lib.rs` for the first-launch sequence.

## Capabilities

### New Capabilities
- `notifications`: the user is informed of errors and significant status changes through the operating system's notification service.
- `autostart`: the application can register itself to launch at login and be unregistered again.

### Modified Capabilities
_None._

## Impact

Depends on `add-settings-store`, `add-dictation-pipeline` and `add-settings-window`. Places a hard
requirement on `add-packaging-ci`: without the Start-menu shortcut carrying the AUMID, Windows toasts
do not appear from an installed build.

## Non-goals

- No in-app notification centre or history.
- No notification actions, buttons or transcript previews in the toast.
- No `SMAppService` on macOS 13+; the LaunchAgent plist matches the reference and works further back.
