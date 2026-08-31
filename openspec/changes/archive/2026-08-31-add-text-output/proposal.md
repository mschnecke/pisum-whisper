## Why

A transcript that stays inside the app is useless. The result has to arrive at the cursor in whatever
application the user was already typing in — which means the clipboard plus a synthetic paste, since
no cross-application API exists to insert text directly.

## What Changes

- Add `ITextOutput`, one service owning the whole delivery sequence: read the previous clipboard,
  set the transcript, paste, restore. The steps are invariants about each other rather than a
  sequence a caller can order, so they do not split into the `IClipboardService` + `IPasteService`
  pair this proposal originally named.
- **The clipboard is native code in `Pisum.Whisper.Platform`** behind a `ISystemClipboard` interface
  declared in Core — Win32 `SetClipboardData`/`GetClipboardData` on Windows, `NSPasteboard` on
  macOS. Avalonia is not an option: in 12.1 the only route to an `IClipboard` is `TopLevel.Clipboard`,
  and this is a tray-only process with no `TopLevel`. This change is the first code in that project.
- Paste through SharpHook's `EventSimulator`: hold Ctrl (Cmd on macOS), click V, release, paced by
  30 ms per edge on macOS. SharpHook's own `IEventSimulator` is the seam, so there is no wrapper
  interface for it.
- **Do not attempt a paste the platform will drop.** `IPasteProbe`, also native, answers whether
  synthetic input can reach the focused application — `AXIsProcessTrusted()` on macOS, an
  integrity check on the foreground window on Windows. Both platforms drop injected input silently
  and report success, so without this the transcript is restored over and lost with no message.
- **Trim the transcript before delivering it.** Neither this project's `GeminiProvider` nor the
  reference trims what the model returns, and Gemini's responses routinely end in a newline, so
  every dictation pastes a stray blank line at the cursor.
- **Save and restore the previous clipboard contents.** The reference overwrites the clipboard on
  every dictation and never puts it back, silently destroying whatever the user had copied. Read the
  previous text, set the transcript, paste, wait, then restore — best-effort, so a failed read never
  blocks the dictation itself, and guarded: never after a failed paste, only if the clipboard still
  holds our transcript, and text only. Cancelling a delivery shortens the wait rather than
  abandoning the restore, so quitting mid-dictation cannot leave the user's clipboard destroyed.
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
`add-dictation-pipeline`, which must await a delivery in progress during shutdown rather than
dropping it, so that the restore is not abandoned, and which should refuse to start a recording
while one is still transcribing, as the reference does — deliveries are serialised here regardless,
but a queued second dictation is a worse experience than being told to wait. On macOS the paste needs the **Accessibility**
permission, and spike S1b failed there on an unsigned build — the paste is still attempted, and
degrades to the clipboard fallback message when it does not land. On Windows, `SendInput` cannot
reach an elevated window from a non-elevated process — that case is caught by the probe where it can
be, and otherwise degrades silently, which is expected behaviour rather than a defect.

Adds `tests/Pisum.Whisper.Platform.Tests` for a manual clipboard round-trip; the sequence logic is
tested in `Core.Tests` with no clipboard and no keyboard.

## Non-goals

- No per-application special-casing and no UI Automation text insertion.
- No typing the transcript keystroke by keystroke (`SimulateTextEntry`) instead of pasting.
- No preserving of non-text clipboard contents — an image or a file list is not round-tripped.
- No transcript history and no re-paste of an earlier result.
- No rich text, formatting or images — plain text only.
- No setting to disable the restore.
