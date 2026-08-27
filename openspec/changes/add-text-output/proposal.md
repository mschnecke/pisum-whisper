## Why

A transcript that stays inside the app is useless. The result has to arrive at the cursor in whatever
application the user was already typing in — which means the clipboard plus a synthetic paste, since
no cross-application API exists to insert text directly.

## What Changes

- Add `IClipboardService` and `IPasteService`.
- Clipboard access through Avalonia's `IClipboard`, marshalled onto the UI thread.
- Paste through SharpHook's `EventSimulator`: hold Ctrl (Cmd on macOS), click V, release.
- **Save and restore the previous clipboard contents.** The reference overwrites the clipboard on
  every dictation and never puts it back, silently destroying whatever the user had copied. Read the
  previous text, set the transcript, paste, wait, then restore — best-effort, so a failed read never
  blocks the dictation itself.
- Preserve the reference's graceful degradation: if the paste keystroke fails, the transcript is
  already on the clipboard, so report "Text was copied to clipboard but paste simulation failed.
  Use Ctrl+V to paste manually" rather than losing the result outright.

Reference: `W:\github-pisum-transcript\src-tauri\src\output\` (`clipboard.rs`, `paste.rs`).

## Capabilities

### New Capabilities
- `text-output`: transcribed text is delivered to the active application at the cursor position, without discarding the user's existing clipboard contents.

### Modified Capabilities
_None._

## Impact

Depends on `bootstrap-solution` (spike S1 covers `EventSimulator`). Unblocks
`add-dictation-pipeline`. On macOS the paste needs the **Accessibility** permission. On Windows,
`SendInput` cannot reach an elevated window from a non-elevated process — that case degrades to the
clipboard fallback message and is expected behaviour, not a defect.

## Non-goals

- No per-application special-casing and no UI Automation text insertion.
- No transcript history and no re-paste of an earlier result.
- No rich text, formatting or images — plain text only.
