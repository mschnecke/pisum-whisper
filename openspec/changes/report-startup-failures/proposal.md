## Why

A tray-only process that fails before its tray icon exists is indistinguishable from one that never
launched. Four failures do this: settings that are invalid JSON (issue #20) or unwritable on a first
launch, a container failing `ValidateOnBuild`, and a missing macOS tray asset.

Three more leave it running but degraded, where nobody looks. An uncreatable log directory silences
the log itself, so the four above lose their fallback. Keys never granted, or withdrawn while
running, reach only the settings window's Hotkey tab.

## What Changes

- Add `IFatalErrorReporter` over a native modal dialog — `MessageBoxW` on Windows,
  `osascript -e 'display dialog'` on macOS. Neither needs Avalonia or a dispatcher, so it runs before
  either exists.
- Guard `Program.LoadSettings`, the container build and the Avalonia start: log `Fatal`, dispose the
  logger so its sink drains, report, exit. Catch broadly, not `SettingsException` —
  `SettingsStore.Write` is unguarded, so issue #20's narrower fix leaves an unwritable file silent.
- Report both degraded conditions from `App.OnFrameworkInitializationCompleted`, beside
  `ShowFirstLaunch` — **not** where they are discovered, before any dispatcher pumps. Both are
  queryable state by then, so nothing is buffered: `LogDirectory` retains the reason `TryCreate`
  discards, and `Availability` is settled once `host.Start()` returns. Both forced.
- Add an availability-changed event to `IGlobalHotkeyService`: withdrawal is recorded in
  `RunHookAsync`'s catch long after `StartAsync` returned, and nothing observes it.

Reference: **none, a finding rather than an omission** — the reference reports through a Tauri
command with a webview always present.

## Capabilities

### New Capabilities
- `startup-diagnostics`: a failure preventing startup reaches the user, not only a log.

### Modified Capabilities
- `global-hotkey`: availability changes are observable and surfaced without opening the settings window.
- `file-logging`: an uncreatable log directory is retained as state, not only logged.

## Impact

Off-sequence, so no number, per `ROADMAP.md`. `add-system-integration` is archived, so both halves
can land now; the dialog half depends on nothing and can go first. Retires change 9's
twice-deferred `HotkeyAvailability.Failed` item and change 11's recorded startup gap. The macOS
dialog needs the Apple Silicon pass 8 to 11 owe.

## Non-goals

- No in-app error console, crash reporter or history.
- No repair of a corrupt settings file, and **no in-memory defaults**: it holds API keys a settings
  window would overwrite.
- No fourth tray icon state; change 9 rejected that.
- No change to the Hotkey tab's banner, already stale on revocation; fixing that is
  `settings-window`'s.
