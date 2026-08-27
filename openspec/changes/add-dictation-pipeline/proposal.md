## Why

Capture, encoding, transcription, hotkey and output all exist by now, but nothing connects them. This
change adds the state machine that turns a keypress into pasted text, and it is where every timing
rule and concurrency guard lives. **This is the change that first makes the product work end to end.**

## What Changes

- Add `DictationOrchestrator`, a singleton reproducing the reference's recording state machine.
- Two recording modes:
  - `holdToRecord` — key down starts, key up stops and transcribes.
  - `toggle` — key down starts or stops; key up ignored.
- Timing rules carried over deliberately, each of which fixes a real failure:
  - **50 ms minimum recording.** Shorter presses are discarded silently, with no notification. An
    accidental brush of the hotkey should do nothing at all, not raise an error.
  - **200 ms toggle debounce.** Without it, keyboard auto-repeat toggles recording on and off rapidly.
  - **50 ms delay** between setting the clipboard and sending the paste keystroke, so the target
    application observes the new contents.
- Concurrency guards: already recording, return silently; transcription in flight, report
  "Transcription In Progress" rather than starting a second overlapping pipeline.
- Max-duration watchdog built on a `CancellationTokenSource` and `Task.Delay`, cancelled when
  recording ends normally. The reference spawns a thread that sleeps the entire duration on every
  recording, leaking one per dictation.
- Wrap the pipeline in `try`/`catch`, mapping `AppException.Category` to user-facing messages. The
  reference's `catch_unwind` guard is dead code in its release profile, which aborts on panic.

Reference: `W:\github-pisum-transcript\src-tauri\src\hotkey\manager.rs`, the 515-line core.

## Capabilities

### New Capabilities
- `dictation-pipeline`: holding or toggling the hotkey records speech and delivers the transcript to the cursor.

### Modified Capabilities
_None._

## Impact

Depends on `add-audio-pipeline`, `add-gemini-transcription`, `add-global-hotkey` and
`add-text-output`. Exposes a recording-state signal that `add-tray-icon` consumes. Everything after
this point is presentation and packaging around a working core.

## Non-goals

- No visual recording indicator — the tray icon is the next change.
- No notifications yet. Call sites are identified here; the implementation ships in `add-system-integration`.
- No transcript editing, preview or confirmation step.
