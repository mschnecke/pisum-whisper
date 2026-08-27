## Why

Once dictation works the app is invisible: no window, no dock icon, no taskbar entry. The user needs
to know it is running, whether it is currently recording, and how to reach settings or quit. In the
reference the tray icon is the *only* recording indicator — there is no HUD and no sound.

## What Changes

- Add an Avalonia `TrayIcon` with a `NativeMenu`: Settings, separator, Quit.
- Two icon states, idle and recording, driven by the orchestrator's recording signal. Those updates
  arrive on background threads, so marshal them through `Dispatcher.UIThread.Post`.
- Dynamic tooltip, `"Pisum Whisper - {active preset}"`, refreshed when the active preset changes.
- Theme handling through `TopLevel.PlatformSettings.GetColorValues()` and its `ColorValuesChanged`
  event to select a light or dark icon variant. One mechanism covers **both** platforms, replacing
  the reference's direct Windows registry read of `AppsUseLightTheme`. On macOS, prefer a template
  image and let AppKit invert it if Avalonia exposes that (spike S3).
- Confirm the application runs correctly with no window ever shown.

Reference: `W:\github-pisum-transcript\src-tauri\src\tray.rs`.

## Capabilities

### New Capabilities
- `tray-icon`: the running application is represented in the system tray or menu bar, showing recording state and offering Settings and Quit.

### Modified Capabilities
_None._

## Impact

Depends on `add-dictation-pipeline` for the recording-state signal, and on spike S3. Unblocks
`add-settings-window`, which the Settings menu item opens. Note that macOS `NSStatusItem` does not
present tooltips the way Windows does; if S3 confirms this, the active preset name needs another
home in the menu itself.

## Non-goals

- No settings window yet. The Settings menu item may be a stub until the next change.
- No recording HUD, overlay, waveform or audible cue.
- No transcript history in the menu.
