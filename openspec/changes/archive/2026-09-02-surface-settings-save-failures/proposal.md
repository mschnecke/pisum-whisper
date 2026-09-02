## Why

`PresetsViewModel.AddAsync`, `SaveAsync`, `ActivateAsync` and `DeleteAsync`
(`src/Pisum.Whisper.App/Settings/ViewModels/PresetsViewModel.cs:74-166`) each call one of
`SettingsStore.SavePreset`/`SetActivePreset`/`DeletePreset` with no `try`/`catch`, and
`SettingsEditor.Save`'s debounced commit (`SettingsEditor.cs:195`, the path the other five tabs write
through) has the same shape. All five reach `SettingsStore.Write`, wrapped in `SettingsException`
since #34's fix — but nothing catches it. An `IAsyncRelayCommand`'s `ICommand.Execute` does not await
its `Task` (CommunityToolkit.Mvvm 8.4.2, no `FlowExceptionsToTaskScheduler` set), and the editor's
commit itself runs unawaited on a pooled thread, so the exception becomes an unobserved task
exception: no crash, no log line, no notification. The window looks like the edit, deletion or
activation worked when the file was never written — disk full, permission denied, or a network drive
gone from under the home directory.

## What Changes

- `SettingsEditor.Commit()` wraps `_store.Save(draft)` in `try`/`catch (SettingsException)`, logs the
  failure and calls `INotificationService.Notify` (forced, not silenced by the tray-notification
  preference) — one change point covering every tab but Presets, which writes through the store
  directly.
- `PresetsViewModel`'s four commands each wrap their `SettingsStore` call the same way, through a
  shared private `ReportSaveFailure` helper that logs, notifies, and calls `Reload()` so the tab shows
  what is actually on disk — the concrete fix for "an edited name still shows the edited text as if
  it had saved."
- Both gain an `INotificationService` constructor dependency; `SettingsWindowViewModel` and
  `SettingsWindowRegistrationTests` are updated to supply it (the latter following
  `NotificationRegistrationTests`' shape, since its `BuildHost` does not register the capability
  today).
- `SettingsEditorTestBase.NewEditor` and `PresetsViewModelTests.NewViewModel` take an optional
  `INotificationService`, defaulting to a new `RecordingNotificationService`, so every existing call
  site compiles unchanged.

Reference: `PresetConfig.svelte`'s four handlers in `W:\github-pisum-transcript\src\components` each
end `catch { // silently ignore }`. This change diverges from that on purpose, in the shape change 11
already established for `DictationFailure`: a failure is described and shown, not swallowed.

## Capabilities

### New Capabilities
_None._

### Modified Capabilities
- `settings-window`: a settings write that fails to reach disk is reported to the user and the
  affected tab reflects what is actually persisted, instead of showing the failed edit as if it had
  saved.

## Impact

Off-sequence, so no number, per `ROADMAP.md`. Code changes: `SettingsEditor`, `PresetsViewModel`,
`SettingsWindowViewModel`, and their tests. No new dependency, no schema change. Closes #37.

## Non-goals

- No change to `SettingsStore`'s own exception wrapping — `fix-startup-ioexception-mislabeling`
  already made every write failure a `SettingsException`; this change only starts catching it.
- No retry of a failed save. The user retries by editing again; the fields that failed to save are
  left as typed (Add) or reverted to the persisted value (Save), never resubmitted automatically.
- No new notification transport or policy. `INotificationService.Notify` already exists and is
  exactly this shape's intended consumer.
