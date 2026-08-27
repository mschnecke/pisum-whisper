## Why

The entire product is driven by one global key binding, and it must report both edges: press starts
recording, release stops it. This rules out the obvious platform APIs — Win32 `RegisterHotKey` fires
`WM_HOTKEY` on press only, and Carbon `RegisterEventHotKey` behaves the same. Hold-to-record cannot
be built on either, so the hotkey layer needs a raw hook rather than a registration API.

## What Changes

- Add `IGlobalHotkeyService` with a SharpHook implementation owning one libuiohook global hook
  (`WH_KEYBOARD_LL` on Windows, `CGEventTap` on macOS).
- Because a raw hook reports every key rather than a registered combination, track the modifier mask
  and match the configured binding ourselves. That is more control than the reference had: the
  settings window's hotkey recorder can later reuse the same hook in a capture mode instead of
  depending on browser key events.
- Add `KeyCodeMap`, a string to `SharpHook.Data.KeyCode` table ported from the reference's key table
  (letters, digits, F1-F12, arrows, punctuation, numpad) plus modifier aliases
  (`ctrl`/`control`, `alt`, `shift`, `meta`/`super`/`win`/`cmd`/`command`).
- Add `ConflictDetector` carrying the reference's system-hotkey table (Ctrl+Alt+Del, Alt+Tab, Alt+F4,
  Win+L/D/E/R/Tab, Ctrl+Shift+Esc, Cmd+Q/W/Tab, Cmd+Shift+3/4/5, Cmd+Space, Ctrl+Space) with
  normalised, order-insensitive comparison. It **warns only and never blocks the binding** — same as
  the reference, because the table is a heuristic and users have legitimate reasons to override it.
- Default binding: Ctrl+Shift+Space, or Cmd+Shift+Space on macOS.

Reference: `W:\github-pisum-transcript\src-tauri\src\hotkey\` (`manager.rs`, `parse.rs`, `conflict.rs`).

## Capabilities

### New Capabilities
- `global-hotkey`: a user-configurable key combination is observed system-wide, reporting both press and release.

### Modified Capabilities
_None._

## Impact

Depends on `bootstrap-solution` (specifically spike S1) and `add-settings-store` for the binding.
Unblocks `add-dictation-pipeline`. On macOS the hook requires the **Accessibility** permission — the
same grant the paste simulation needs, so it costs the user nothing extra, but it must be requested
and handled here rather than assumed.

## Non-goals

- No hotkey recorder UI — that ships with the settings window; this change exposes the capture mode it will use.
- No per-preset bindings and no multiple simultaneous bindings.
- No mouse buttons or media keys.
