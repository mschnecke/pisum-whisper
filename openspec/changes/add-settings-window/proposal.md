## Why

Every setting the app reads is, so far, only editable by hand-writing JSON. The user needs a way to
add a Gemini API key, pick a model, edit preset prompts, change the hotkey and adjust logging —
without which the product is not usable by anyone but its author.

## What Changes

- Add a single Avalonia settings window: 700x540, minimum 540x400, resizable, not maximizable,
  centred, **created hidden**, and **closing hides it rather than quitting** the app.
- Six tabs, ported from the reference's Svelte components:
  - **Providers** — add and remove Gemini entries; masked API key with a reveal toggle; model
    dropdown populated from `ListModelsAsync` with a Refresh button; enable toggle; Test Connection
    with an inline result.
  - **Presets** — CRUD with Built-in and Active badges; built-in presets cannot be deleted; inline edit.
  - **Hotkey** — current binding plus Change, which switches to a live recorder (at least one
    modifier required, Escape cancels) reusing the global hook's capture mode; conflict warning banner.
  - **Audio** — Opus or WAV.
  - **Logging** — level, max file size (1-100 MB), retention (1-365 days), log path, Open Log Folder.
  - **General** — start with system, show notifications, recording mode, max duration (10-3600 s).
- Keep the reference's **save-on-change** model. There is no OK, Cancel or Apply: every edit persists
  immediately and re-applies live — rebuild the provider pool, hot-swap the log level, re-register
  the hotkey, refresh the tray tooltip. A tray utility the user visits rarely should never lose an
  edit to a forgotten Apply button.
- MVVM with `CommunityToolkit.Mvvm` source generators.

The reference reaches its backend through 20 IPC commands; here these are direct service calls, so
no IPC layer is needed.

Reference: `W:\github-pisum-transcript\src\components\` and the window block of `tauri.conf.json`.

## Capabilities

### New Capabilities
- `settings-window`: all application settings are viewable and editable in a window reachable from the tray, applying immediately.

### Modified Capabilities
_None._

## Impact

Depends on `add-settings-store`, `add-file-logging`, `add-gemini-transcription`, `add-global-hotkey`
and `add-tray-icon`. This is the largest UI change in the sequence and the last one before the app is
usable by someone other than its author.

**It also depends on `migrate-tests-to-xunit-v3`, which is not one of the numbered changes.** Avalonia
ships first-party headless test integration for xUnit and NUnit only — there is no
`Avalonia.Headless.MSTest` and there never has been — so testing this window meant leaving MSTest
first. That migration is done, and it pinned `xunit.v3` to **3.2.2** because that is the version
`Avalonia.Headless.XUnit` 12.1.1 is compiled against; taking 4.0.0 resolves silently against a major
it was not built for. Add the `Avalonia.Headless.XUnit` reference here and write `[AvaloniaFact]`
tests — the migration deliberately added neither. Re-read that package's dependency group when this
change opens: if Avalonia has moved to xunit.v3 4.x, bump both together.

## Non-goals

- No dark theme. The reference's settings window is light-only and this is not the change to fix that.
- No localization; strings stay hardcoded English.
- No input device picker.
- No onboarding wizard or About dialog.
