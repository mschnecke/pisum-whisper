## Current behavior

Five call sites reach `SettingsStore.Write` with nothing downstream observing a failure:

- `SettingsEditor.Commit()` (`src/Pisum.Whisper.App/Settings/SettingsEditor.cs:171-196`) calls
  `_store.Save(draft)` from `CommitAfterQuietAsync`, itself started (not awaited) from `Edit()` and
  run as a continuation of `Task.Delay` — genuinely async, on a pooled thread. `FlushAsync()` awaits
  `_commit`, but only `PresetsViewModel` ever calls it; the other five tabs (Audio, General, Hotkey,
  Logging, Providers) edit and never await anything, so a `Commit()` failure is unobserved for them
  unconditionally.
- `PresetsViewModel.AddAsync`, `SaveAsync`, `ActivateAsync`, `DeleteAsync`
  (`src/Pisum.Whisper.App/Settings/ViewModels/PresetsViewModel.cs:74-166`) each `await
  _editor.FlushAsync()` and then call `_store.SavePreset`/`SetActivePreset`/`DeletePreset` directly,
  bypassing the editor's clone-and-debounce path entirely (by design — see the class remarks on why
  Presets does not write through `SettingsEditor`). None of the four is wrapped in `try`.

`SettingsStore.Save` (`SettingsStore.cs:103-108`) calls `Write` *before* replacing `Current`, so a
failed write leaves `Current` — and therefore every already-rendered view model bound to it —
referentially untouched. The Presets tab's own fields (`PresetEntryViewModel.Name`,
`SystemPrompt`) are bound directly to the entry the user is editing, though, so a failed `SaveAsync`
leaves the *visible* text as the failed edit while `Store.Current` still holds the old value: the tab
shows a save that did not happen.

Because `ICommand.Execute` on an `IAsyncRelayCommand` does not await its `Task` (no
`AsyncRelayCommandOptions.FlowExceptionsToTaskScheduler` is set anywhere in this codebase), and
nothing subscribes to `TaskScheduler.UnobservedTaskException`, every one of these five failures
today is a silent no-op.

## Fix

**`SettingsEditor.Commit()`** gets a `try`/`catch (SettingsException)` around the one `_store.Save`
call, logging at `Error` and calling `_notifications.Notify(SaveFailureTitle, exception.Message)`.
`exception.Message` is safe to show as-is — it is `SettingsStore.Write`'s own composed string naming
the file path, the same message `StartupFailure.Describe`'s `SettingsException` arm already shows
verbatim. This one change point covers every tab that writes through the editor, including Presets'
own `FlushAsync()` when it is flushing a *different* tab's pending edit (e.g. `MaxRecordingDurationSecs`,
which every Presets test edits before asserting the flush-first ordering) — after this change,
`Commit()` no longer rethrows, so `FlushAsync()` completes normally whether or not the flushed commit
actually reached disk, and the failure it hid is reported at the source instead.

**`PresetsViewModel`** gets a private helper:

```csharp
private const string SaveFailureTitle = "Settings Not Saved";

private void ReportSaveFailure(SettingsException exception, string message, params object?[] args)
{
    _logger.LogError(exception, message, args);
    _notifications.Notify(SaveFailureTitle, exception.Message);
    Reload();
}
```

Each command wraps its store call:

```csharp
try
{
    _store.SavePreset(preset);
}
catch (SettingsException exception)
{
    ReportSaveFailure(exception, "Preset '{PresetName}' could not be added.", preset.Name);
    return;
}
```

`Reload()` is called unconditionally on failure, in every one of the four commands, rather than only
where a visible desync is possible. It rebuilds `Presets` and `Selected` from `Store.Current` — which
a failed `Save()` left untouched — so `SaveAsync`'s bound text reverts to the persisted value (the
concrete fix for the issue's "an edited name still shows the edited text as if it had saved"), while
for `AddAsync`, `ActivateAsync` and `DeleteAsync` it is a harmless re-render of a list that was never
optimistically changed. One shape for all four is simpler than deciding per-command whether reload is
needed, and it is never wrong. `Reload()` never touches `NewName`/`NewSystemPrompt`, so `AddAsync`'s
typed-but-unsaved fields survive the failure for the user to retry without retyping.

`DeleteAsync`'s existing remarks ("no `try` here" because `CanDeleteSelected` gates the button) stay
correct for the *validation* throw (`DeletePreset`'s "built-in cannot be deleted") — that one really
is unreachable — but no longer describe the method accurately once a `try` exists for the *write*
failure, which no `CanExecute` guard can rule out. The remarks are reworded to say so.

## Why one shared title, not per-command wording

`SettingsEditor` and `PresetsViewModel` both show `"Settings Not Saved"` rather than two similar but
different titles ("Preset Not Saved" vs. "Settings Not Saved"). They are the same failure — a write
to `~/.pisum-whisper.json` did not reach disk — reached through two call paths for one reason (Presets
bypasses the debounced editor by design); showing two titles for one cause would read as two
different problems. The message underneath still differs naturally, since it is `exception.Message`
composed by `SettingsStore.Write` at the point of failure, always naming the file.

## Platform

No platform split. `SettingsEditor`, `PresetsViewModel` and `INotificationService` are all in
`Pisum.Whisper.App`/`Pisum.Whisper.Core`, neither of which is platform-specific; `Notify`'s transport
(`ToastPresenter`) already handles the Windows/macOS placement difference and needs no change here.

## Rejected alternatives

- **One shared helper across both classes** (e.g. a static `SettingsSaveFailure.Report(...)`).
  Rejected: the two call sites differ in what they log (a count of changed fields vs. a preset name
  or id) and in whether they call `Reload()` afterwards (`SettingsEditor` has no view state to
  refresh), so a shared helper would need parameters for both differences and end up no smaller than
  the two independent five-line blocks it replaces.
- **Have `SettingsEditor.Commit()` rethrow after logging, and let `FlushAsync()`'s caller catch it.**
  Rejected: `FlushAsync()` has exactly one caller today (`PresetsViewModel`), so this would report a
  *different* tab's flushed failure through the Presets tab's own catch, attributing it to the wrong
  command, and would leave the five tabs that never call `FlushAsync()` uncovered exactly as they are
  today.
- **Retry the write once before giving up.** Rejected: the issue names disk-full and permission-denied
  as the likely causes, neither of which a same-process immediate retry resolves; `GeminiProvider`'s
  three-attempt retry exists for a genuinely transient failure (network, rate limit), which a local
  file write is not.

## Tests

- `tests/Pisum.Whisper.App.Tests/Settings/SettingsEditorTests.cs`: a new case opens the store's
  settings file with an exclusive lock (`FileShare.None`) before editing, so `Write`'s `File.Move`
  fails with a real `IOException`; asserts the commit completes (no unobserved exception), the
  supplied `RecordingNotificationService` recorded one forced notification, and `Store.Current` is
  unchanged.
- `tests/Pisum.Whisper.App.Tests/ViewModels/PresetsViewModelTests.cs`: one new case per command
  (Add/Save/Activate/Delete), each using the same file-lock technique around the command's
  `ExecuteAsync`, asserting: the notification fired, `Reload()` ran (the list reflects
  `Store.Current`, unchanged), and — for Save specifically — the previously-bound `Selected.Name`
  reverted to the persisted value rather than keeping the failed edit.
- `tests/Pisum.Whisper.App.Tests/Settings/SettingsWindowRegistrationTests.cs`: `BuildHost()` gains
  `AddNotifications()` plus the `ToastPresenter`/`INotificationPresenter` registration
  (`NotificationRegistrationTests`' shape), since `SettingsEditor` and `PresetsViewModel` now require
  `INotificationService`; the existing `TheRegistrationSatisfiesContainerValidation` case is what
  would fail without it.
- `TheStoresRefusalToDeleteABuiltin_IsNotReachableFromTheUi`'s comment is reworded: the guard still
  makes the *validation* throw unreachable, but a `try` now exists in `DeleteAsync` for the write
  failure, so "its lack of a try" is no longer true.
