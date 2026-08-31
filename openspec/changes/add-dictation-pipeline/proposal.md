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
  - **200 ms toggle debounce.** The reference introduced it against keyboard auto-repeat, which
    change 6's `HotkeyMatcher` already absorbs without raising an edge. It is kept for what it still
    covers: a fumbled double-tap in toggle mode, long enough to escape the 50 ms discard and so
    reach transcription and fail there. See `design.md`.
- Concurrency guards: already recording, return silently; transcription in flight, report
  "Transcription In Progress" rather than starting a second overlapping pipeline.
- Max-duration watchdog built on a `CancellationTokenSource` and `Task.Delay`, cancelled when
  recording ends normally. The reference spawns a thread that sleeps the entire duration on every
  recording, leaking one per dictation.
- A total transcription budget, because the 60 s per-request timeout does not bound a dictation:
  retries and provider fallback multiply it to minutes, during which the hotkey does nothing and
  says nothing.
- Wrap the pipeline in `try`/`catch`, describing each failure as a user-facing title and message.
  There is no `AppException` in this codebase — `AudioException`, `TextOutputException` and
  `OperationCanceledException` are told apart by type, and `TranscriptionException` by its
  `ErrorCategory`. The reference's `catch_unwind` guard is dead code in its release profile, which
  aborts on panic; the hazard here is different, an escaping exception being swallowed as an
  unobserved task exception that wedges the state machine silently.

Reference: `W:\github-pisum-transcript\src-tauri\src\hotkey\manager.rs`, the 515-line core.

## Capabilities

### New Capabilities
- `dictation-pipeline`: holding or toggling the hotkey records speech and delivers the transcript to the cursor.

### Modified Capabilities
_None._

## Impact

Depends on `add-audio-pipeline`, `add-gemini-transcription`, `add-global-hotkey` and
`add-text-output`. Exposes a three-valued state signal — idle, recording, transcribing — that
`add-tray-icon` consumes. Everything after this point is presentation and packaging around a working
core.

## Non-goals

- No visual recording indicator — the tray icon is the next change.
- No notifications yet. This change decides *which* failure each one is and *what* it says, and logs
  it; `add-system-integration` adds the transport and the forced-versus-suppressible policy, and in
  doing so **modifies** this capability rather than filling in markers left for it.
- No change to the settings schema. The transcription budget is a constant.
- No transcript editing, preview or confirmation step.
