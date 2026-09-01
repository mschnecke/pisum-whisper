## Why

Every setting the app reads is, so far, only editable by hand-writing JSON. The user needs a way to
add a Gemini API key, pick a model, edit preset prompts, change the hotkey and adjust logging —
without which the product is not usable by anyone but its author.

## What Changes

- Add a single Avalonia settings window: 700x540, minimum 540x400, resizable, not maximizable,
  centred, opened from the tray, and **closing hides it rather than quitting** the app.
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
  by itself. A tray utility the user visits rarely should never lose an edit to a forgotten Apply
  button. Edits are applied to a clone of the settings and written after a short idle delay, so a
  typed API key costs one file write rather than one per keystroke.
- MVVM with `CommunityToolkit.Mvvm` source generators.

**Applying an edit needs no new plumbing.** `SettingsStore.Changed` already has its subscribers:
change 3 hot-swaps the log level, change 6 rebinds the hotkey matcher, change 9 refreshes the tray
tooltip, and `GeminiProviderPool` and `DictationOrchestrator` read `SettingsStore.Current` per use.
Nothing calls `Save` at runtime today, so this change is the window and only the window. In
particular the provider pool is **not** rebuilt — that is the reference's `apply_settings` shape and
this codebase deliberately rejected it.

The reference reaches its backend through 20 IPC commands; here these are direct service calls, so
no IPC layer is needed.

Reference: `W:\github-pisum-transcript\src\components\` and the window block of `tauri.conf.json`.

## Capabilities

### New Capabilities
- `settings-window`: all application settings are viewable and editable in a window reachable from the tray, applying immediately.

### Modified Capabilities
- `settings-persistence`: the cached settings are **replaced** on every write rather than mutated in
  place, so a preset edit cannot disturb a component reading settings on another thread. The three
  preset operations are the only writers that mutate the cached graph, and this change is the first
  thing that calls them at runtime — with a settings window open during a dictation, adding or
  deleting a preset can invalidate the enumeration the transcription path uses to resolve the active
  preset's prompt, and lose the dictation.

## Impact

Depends on `add-settings-store`, `add-file-logging`, `add-gemini-transcription`, `add-global-hotkey`
and `add-tray-icon`. This is the largest UI change in the sequence and the last one before the app is
usable by someone other than its author. It also picks up the two items change 9 deferred: the
`TrayIcon.Clicked` handler, and proving the tray tooltip follows a preset change.

**It also depends on `migrate-tests-to-xunit-v3`, which is not one of the numbered changes.** Avalonia
ships first-party headless test integration for xUnit and NUnit only — there is no
`Avalonia.Headless.MSTest` and there never has been — so testing this window meant leaving MSTest
first. That migration is done. Its standing question is now **answered**: `Avalonia.Headless.XUnit`
12.1.1 depends on `xunit.v3.extensibility.core [3.2.2, )`, so the 3.2.2 pin still satisfies it and no
coordinated bump is needed. Add the `Avalonia.Headless.XUnit` reference here and write
`[AvaloniaFact]` tests — the migration deliberately added neither.

## Non-goals

- No dark theme. The reference's settings window is light-only and this is not the change to fix that.
- No localization; strings stay hardcoded English.
- No input device picker.
- No onboarding wizard or About dialog.
- No autostart or notification *behaviour*. The General tab persists both flags; change 11 acts on them.
