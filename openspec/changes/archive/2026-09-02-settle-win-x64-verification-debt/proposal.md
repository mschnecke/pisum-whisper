## Why

Seven archived changes carry eleven by-hand checks that need only this Windows machine, filed under
an Apple Silicon sitting they do not belong to; issue #30 separates them. Two are worth more than a
tick: change 8's 6.3 is the only measurement of the transcription timeouts, and change 11's 7.4 is a
startup failure Avalonia's source predicts whenever a notification is raised from a pooled thread
before the UI exists.

## What Changes

- Run the eleven checks, change 8's 6.1 first, recording each in the archived change that asked it
  and ticking its box there.
- Time four recordings up to the 600 s maximum; move `GeminiHttpClient.Timeout` once if a
  maximum-length attempt exceeds half of it, the budget following. Judge `SettingsEditor.CommitDelay`
  during the first dictation; move it once if wrong.
- Answer 7.4 by a forced-ordering experiment in `Program.Main`; Avalonia's source predicts a fatal
  startup failure, so `ToastPresenter` gains a gate that drops a notification raised before the main
  thread owns a dispatcher, with headless tests.
- Keep the fatal-dialog driver as `spikes -- fatal`, and correct one archived task: the tray PNGs
  are compiled into the assembly, so the missing-asset reproduction moves the source file.
- Reconcile `ROADMAP.md`, `README.md` and `CLAUDE.md`, add three standing decisions on verification
  debt, and close #30.

Reference: **none.** The retry shape comes from `gemini.rs`, which sets no timeout at all.

## Capabilities

### New Capabilities
_None._

### Modified Capabilities
- `notifications`: a notification raised before the interface exists must not prevent the
  application from starting or from showing later ones.
- `dictation-pipeline`: the budget must accommodate the longest recording the settings allow, and
  its number is measured rather than derived.

## Impact

Off-sequence, so no number, per `ROADMAP.md`. Blocks nothing. Code changes: the gate in
`ToastPresenter`, at most two constants if the measurements move them, and one spike outside the
solution. A change, not a "Record …" commit against #30: a gate, two spec deltas and new decisions
have no home in a commit. Seven archived `design.md` and `tasks.md` files are edited, as on
2026-09-02. The settings file and the `Run` key are touched by four checks and restored after each.

## Non-goals

- Nothing that needs Apple Silicon; that is issue #31.
- Not change 1's 1.5a, which waits on a 44.1 kHz input device, not a platform.
- No fix for a toast found in the alt-tab list (recorded) or for the hotkey-rebind log line on
  every save (`global-hotkey`'s).
- No length-dependent budget or request timeout; 3.1 measures the slope a follow-up would start from.
- No new transport, no packaging, no CI; no automating a check that needs a person.
