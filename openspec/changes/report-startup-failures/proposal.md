## Why

A tray-only process that fails before its tray icon exists is indistinguishable from one that never
launched. Four failures do this today: an uncreatable log directory, a settings file that is not
valid JSON (issue #20), a container that fails `ValidateOnBuild`, and a missing macOS tray asset. The
first is the worst, because it removes the log as well — a release build then produces no output
anywhere at all.

Two more are not fatal but leave the hotkey dead: access to observe keys never granted, or withdrawn
while running. Both are reported only in the settings window's Hotkey tab, which change 10's own
remarks call "a smaller thing than telling a user who never opens this window".

## What Changes

- Add `IFatalErrorReporter` over a native modal dialog — `MessageBoxW` on Windows,
  `osascript -e 'display dialog'` on macOS. Both are P/Invoke or `Process.Start` and need no Avalonia
  and no dispatcher, which is what lets them run before either exists.
- Guard `Program.LoadSettings`, the container build and the Avalonia start: log `Fatal`, dispose the
  logger so its asynchronous sink drains, report, exit. Issue #20's four-line fix is a strict subset.
- Report a log-directory failure through the same reporter — the one case in which the log cannot.
- Surface `HotkeyAvailability` other than `Available` through `add-system-integration`'s notification
  transport, leaving the Hotkey tab banner exactly as it is.

Reference: **none, which is a finding rather than an omission.** The reference returns a `Result`
into a Tauri command called from its Svelte frontend, so it always has a webview to show the error in.
Invented rather than re-expressed, as the clipboard restore was.

## Capabilities

### New Capabilities
- `startup-diagnostics`: a failure that prevents startup is reported to the user, not only to a log that may not exist.

### Modified Capabilities
- `global-hotkey`: a binding that cannot be observed is surfaced without the user opening the settings window.

## Impact

Off-sequence, so no number, per `ROADMAP.md`'s own rule. The notification half depends on
`add-system-integration`; the dialog half depends on nothing and can land first. Retires change 9's
twice-deferred `HotkeyAvailability.Failed` item and the startup gap in
`add-system-integration`'s *Risks*.

## Non-goals

- No in-app error console, crash reporter or error history.
- No repair of a corrupt settings file, and **no starting with in-memory defaults**: it holds API
  keys a settings window would then overwrite.
- No fourth tray icon state; change 9 rejected that.
- No change to the Hotkey tab's banner.
