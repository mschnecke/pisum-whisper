## 1. Wrap `SettingsStore.Write`'s failure

- [x] 1.1 In `src/Pisum.Whisper.Core/Settings/SettingsStore.cs`, wrap `Write`'s `File.WriteAllText` +
  `File.Move` in a `try`/`catch (Exception exception) when (exception is IOException or
  UnauthorizedAccessException)` that rethrows `new SettingsException($"The settings file '{FilePath}'
  could not be written: {exception.Message}", exception)` — the message `StartupFailure.Describe`'s
  deleted arm used to compose, now composed once at the source instead. Update `Load`'s
  `<exception cref="SettingsException">` doc comment to also cover an unwritable file, since `Load`
  calls `Write` on first launch. Add
  `Load_WhenTheDirectoryDoesNotExist_ThrowsSettingsExceptionNamingThePath` to
  `tests/Pisum.Whisper.Core.Tests/Settings/SettingsStoreTests.cs`: point a `SettingsStore` at
  `Path.Combine(_directory, "missing", ".pisum-whisper.json")` (the `missing` subdirectory
  deliberately not created) and assert `Load()` throws `SettingsException` whose message contains the
  path and whose `InnerException` is the original `DirectoryNotFoundException`. Verify:
  `dotnet test tests/Pisum.Whisper.Core.Tests --filter-class '*SettingsStoreTests'` green;
  `dotnet build Pisum.Whisper.slnx` at 0 warnings.

## 2. Narrow `StartupFailure.Describe`

- [x] 2.1 In `src/Pisum.Whisper.Core/Diagnostics/StartupFailure.cs`, delete the
  `UnauthorizedAccessException or IOException => (SettingsErrorTitle, ...)` arm and its comment —
  task 1.1 means nothing reaches this switch under those raw types for an actual settings failure any
  more, and the arm's only remaining effect was mislabeling unrelated I/O failures. Leave
  `SettingsException => (SettingsErrorTitle, exception.Message)` and the `_ => (StartupErrorTitle,
  StartupErrorMessage)` fallback as they are. Verify: `dotnet build Pisum.Whisper.slnx` at 0 warnings
  (the file does not yet compile against the test changes in task 3 until that task lands in the same
  commit).

## 3. Update `StartupFailureTests`

- [x] 3.1 In `tests/Pisum.Whisper.Core.Tests/Diagnostics/StartupFailureTests.cs`, rebuild
  `AnUnwritableSettingsFileIsASettingsError` around a `SettingsException` constructed the way
  `Write` now builds one — `new SettingsException($"The settings file '...' could not be written:
  {inner.Message}", inner)` for each of the two `denied` cases — replacing the raw
  `UnauthorizedAccessException`/`IOException` and its now-stale "Not reachable through
  SettingsException" comment. Add
  `ANonSettingsIOExceptionIsAStartupError`: describe a raw, unwrapped
  `new FileNotFoundException("The resource avares://Pisum.Whisper.App/Assets/tray-idle.png could not
  be found.")` — issue #34's exact reproduction — and assert the title is `"Startup Error"`, the
  message is `StartupFailure.StartupErrorMessage` plus the log pointer, and the message contains
  neither `"settings"` (case-insensitive) nor `".pisum-whisper.json"`. Verify:
  `dotnet test tests/Pisum.Whisper.Core.Tests --filter-class '*StartupFailureTests'` green, including
  the new case failing against the pre-task-2.1 code and passing after it.

## 4. Whole-suite check

- [x] 4.1 `dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual` green, and
  `dotnet build Pisum.Whisper.slnx` at 0 warnings. Verify: both commands succeed with no other test
  outside `SettingsStoreTests` and `StartupFailureTests` changing outcome.
