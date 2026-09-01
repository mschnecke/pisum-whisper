## Why

Once dictation works the app is invisible: no window, no dock icon, no taskbar entry. The user needs
to know it is running, whether it is currently recording, and how to reach settings or quit. In the
reference the tray icon is the *only* recording indicator — there is no HUD and no sound.

## What Changes

- Add an Avalonia `TrayIcon` with a `NativeMenu`: Settings, separator, Quit.
- Three icon states — idle, recording and transcribing — driven by `DictationOrchestrator.StateChanged`.
  Three, not two: change 8 publishes three `DictationState` values precisely so the icon need not
  claim to be recording during the upload, which is the reference's defect. Those updates arrive on
  background threads, so marshal them through `Dispatcher.UIThread.Post`.
- Dynamic tooltip, `"Pisum Whisper - {active preset}"`, refreshed when the active preset changes.
- **No theme handling.** No probe, no `ColorValuesChanged` subscription, no light/dark variants.
  macOS sets `TrayIcon.IsTemplateIcon` and lets AppKit tint the glyph — the Apple-recommended
  treatment, and less code than probing; Avalonia 12.1.1 does expose it, undocumented. Windows
  carries the contrast in the art instead, because every theme value Avalonia can reach reports the
  *apps* theme while the Windows 11 taskbar follows a different key. See `design.md`.
- Confirm the application runs correctly with no window ever shown.

Reference: `W:\github-pisum-transcript\src-tauri\src\tray.rs`.

## Capabilities

### New Capabilities
- `tray-icon`: the running application is represented in the system tray or menu bar, showing recording state and offering Settings and Quit.

### Modified Capabilities
_None._

## Impact

Depends on `add-dictation-pipeline` for the recording-state signal, and on spike S3. Unblocks
`add-settings-window`, which the Settings menu item opens. S3 has since run on an Apple M4 and
recorded the `NSStatusItem` tooltip as **PASS**, so the tooltip stays a tooltip and the active preset
name does not need another home in the menu.

## Non-goals

- No settings window yet. The Settings menu item may be a stub until the next change.
- No recording HUD, overlay, waveform or audible cue.
- No transcript history in the menu.
