## 1. Report a failed commit from `SettingsEditor`

- [x] 1.1 In `src/Pisum.Whisper.App/Settings/SettingsEditor.cs`, add a required
  `Pisum.Whisper.Core.Notifications.INotificationService notifications` parameter to both
  constructors (the public two-argument one and the internal one that also takes the delay
  delegate), store it in a new `_notifications` field, and add `private const string SaveFailureTitle
  = "Settings Not Saved";`. Wrap `Commit()`'s `_store.Save(draft)` call in `try { ... } catch
  (SettingsException exception) { _logger.LogError(exception, "{ChangedSettings} settings changes
  could not be saved.", edits); _notifications.Notify(SaveFailureTitle, exception.Message); }` —
  logged at `Error`, right after the existing `Debug` "Committing" line. `Commit()` no longer
  rethrows on failure. In `tests/Pisum.Whisper.App.Tests/Settings/SettingsEditorTestBase.cs`, add an
  optional `INotificationService? notifications = null` parameter to `NewEditor`, defaulting to `new
  RecordingNotificationService()` (already defined in `Pisum.Whisper.App.Tests`), and pass it through
  to the new constructor argument. Verify: `dotnet build Pisum.Whisper.slnx` at 0 warnings (every
  existing `NewEditor()` / `NewEditor(logger)` call site still compiles unchanged since the new
  parameter is optional and comes after the existing ones).

- [x] 1.2 In `tests/Pisum.Whisper.App.Tests/Settings/SettingsEditorTests.cs`, add
  `ACommitThatCannotBeWritten_NotifiesAndLogsRatherThanThrowingUnobserved`: construct a
  `RecordingNotificationService`, get an editor via `NewEditor(notifications: ...)`, open
  `Store.FilePath` with `File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)` to force
  the next `File.Move` to fail with a real `IOException`, then `editor.Edit(...)` and
  `CompleteQuietWindow()` inside the `using` block holding the lock. Assert (after releasing the
  lock, via `WaitForAsync`) that `notifications.Forced` has exactly one entry, its title is
  `"Settings Not Saved"`, `Saves` is still `0`, and `Store.Current` is unchanged. This exercises the
  exact "network drive gone from under the home directory" case #37 describes, without deleting the
  test's own temp directory (which `SettingsEditorTestBase.Dispose()` still needs afterwards).
  Verify: `dotnet test tests/Pisum.Whisper.App.Tests --filter-class '*SettingsEditorTests'` green,
  including the new case failing against the pre-1.1 code (an unobserved `AggregateException`) and
  passing after it.

## 2. Report a failed write from `PresetsViewModel`

- [x] 2.1 In `src/Pisum.Whisper.App/Settings/ViewModels/PresetsViewModel.cs`, add a required
  `INotificationService notifications` constructor parameter, store it in `_notifications`, and add
  `private const string SaveFailureTitle = "Settings Not Saved";` and:
  ```csharp
  private void ReportSaveFailure(SettingsException exception, string message, params object?[] args)
  {
      _logger.LogError(exception, message, args);
      _notifications.Notify(SaveFailureTitle, exception.Message);
      Reload();
  }
  ```
  Wrap each command's store call and route its `SettingsException` to this helper, `return`ing
  immediately after: `AddAsync`'s `_store.SavePreset(preset)` →
  `"Preset '{PresetName}' could not be added."` with `preset.Name`, skipping the field-clearing and
  success log on failure; `SaveAsync`'s `_store.SavePreset(preset)` →
  `"Preset '{PresetName}' could not be saved."` with `preset.Name`; `ActivateAsync`'s
  `_store.SetActivePreset(id)` → `"Preset {PresetId} could not be activated."` with `id`;
  `DeleteAsync`'s `_store.DeletePreset(id)` → `"Preset {PresetId} could not be deleted."` with `id`.
  Reword the class remarks' "Every command flushes the editor first" paragraph and `DeleteAsync`'s own
  remarks to say a `try` now exists for the write failure, while the validation throw
  (`DeletePreset`'s built-in refusal) stays unreachable behind `CanDeleteSelected`. Verify: `dotnet
  build Pisum.Whisper.slnx` at 0 warnings.

- [x] 2.2 In `src/Pisum.Whisper.App/Settings/ViewModels/SettingsWindowViewModel.cs`, add an
  `INotificationService notifications` constructor parameter and pass it to `new
  PresetsViewModel(store, editor, notifications, loggers.CreateLogger<PresetsViewModel>())`. In
  `tests/Pisum.Whisper.App.Tests/ViewModels/PresetsViewModelTests.cs`, add an optional
  `INotificationService? notifications = null` parameter to the file's `NewViewModel` helper,
  defaulting to `new RecordingNotificationService()`, and pass it through. Verify: `dotnet build
  Pisum.Whisper.slnx` at 0 warnings.

- [x] 2.3 In `tests/Pisum.Whisper.App.Tests/ViewModels/PresetsViewModelTests.cs`, add one case per
  command using the `FileShare.None` lock technique from task 1.2 around `ExecuteAsync`:
  `Add_WhenTheWriteFails_KeepsTheTypedFieldsAndNotifies` (fields still hold what was typed, no preset
  added, one forced notification); `Save_WhenTheWriteFails_RevertsTheDisplayedTextAndNotifies`
  (`viewModel.Selected.Name`/`SystemPrompt` — after `Reload()` — equal the *persisted* pre-edit
  values, not what was typed, which is the direct regression guard for the issue's "shows the edited
  text as if it had saved"); `Activate_WhenTheWriteFails_LeavesTheActivePresetAndNotifies`;
  `Delete_WhenTheWriteFails_LeavesThePresetAndNotifies`. Also reword
  `TheStoresRefusalToDeleteABuiltin_IsNotReachableFromTheUi`'s comment: the guard still makes the
  *validation* `SettingsException` unreachable, but `DeleteAsync` now has a `try` for the write
  failure, so "its lack of a try" no longer holds. Verify: `dotnet test
  tests/Pisum.Whisper.App.Tests --filter-class '*PresetsViewModelTests'` green, including all four new
  cases failing against the pre-2.1 code and passing after it.

## 3. Register `INotificationService` where the window's container needs it

- [x] 3.1 In `tests/Pisum.Whisper.App.Tests/Settings/SettingsWindowRegistrationTests.cs`, add
  `builder.Services.AddNotifications(); builder.Services.AddSingleton<ToastPresenter>();
  builder.Services.AddSingleton<INotificationPresenter>(provider =>
  provider.GetRequiredService<ToastPresenter>());` to `BuildHost()`, in
  `NotificationRegistrationTests.BuildHost()`'s shape (`using Pisum.Whisper.App.Notifications;` and
  `using Pisum.Whisper.Core.Notifications;` added to the file). No production `Program.cs` change is
  needed: `AddNotifications()` already runs before `AddSingleton<SettingsWindowViewModel>()` there.
  Verify: `dotnet test tests/Pisum.Whisper.App.Tests --filter-class
  '*SettingsWindowRegistrationTests'` green — in particular
  `TheRegistrationSatisfiesContainerValidation`, which is what a missing registration would fail with
  `ValidateOnBuild` naming `INotificationService`.

## 4. Whole-suite check

- [x] 4.1 `dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual` green, and `dotnet build
  Pisum.Whisper.slnx` at 0 warnings. Verify: both commands succeed with no test outside
  `SettingsEditorTests`, `PresetsViewModelTests` and `SettingsWindowRegistrationTests` changing
  outcome.
