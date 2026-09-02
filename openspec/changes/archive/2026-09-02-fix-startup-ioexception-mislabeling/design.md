## Current behavior

`StartupFailure.Describe(Exception exception, string? logFilePath)` in
`src/Pisum.Whisper.Core/Diagnostics/StartupFailure.cs` switches on exception *type*:

```csharp
SettingsException => (SettingsErrorTitle, exception.Message),
UnauthorizedAccessException or IOException =>
    (SettingsErrorTitle, $"The settings file could not be written: {exception.Message}"),
_ => (StartupErrorTitle, StartupErrorMessage),
```

The second arm exists because `SettingsStore.Write` (`src/Pisum.Whisper.Core/Settings/SettingsStore.cs`)
is a bare `File.WriteAllText` + `File.Move`, called unguarded from `Load()` on first launch, so an
unwritable home directory reaches `Program.Main`'s catch as a raw `UnauthorizedAccessException` or
`IOException` rather than a `SettingsException`. `Read()`, by contrast, already wraps its own
`File.ReadAllText` and `JsonSerializer.Deserialize` failures in `SettingsException` (lines 201-224 as
of `ec8fe69`).

`IOException` has a wide subclass tree — `FileNotFoundException`, `DirectoryNotFoundException`,
`PathTooLongException`, and more — and .NET libraries raise it for failures that have nothing to do
with settings. Issue #34 reproduced this via `Avalonia.Platform.StandardAssetLoader.Open`, called
from `App.LoadIcon` (`src/Pisum.Whisper.App/App.cs:260`) when a tray icon PNG embedded as an
`avares://` resource is missing at build time — a `System.IO.FileNotFoundException`, caught by the
same `IOException` arm and mislabeled "Settings Error" naming a file (`~/.pisum-whisper.json`) that
was never touched.

## Fix

Wrap `SettingsStore.Write`'s failure exactly as `Read()` wraps its own, so **every** settings
failure — read or write, first launch or a later `Save` — arrives at `StartupFailure.Describe` as a
`SettingsException`:

```csharp
private void Write(AppSettings settings)
{
    var json = JsonSerializer.Serialize(settings, SettingsJsonContext.OnDisk.AppSettings);
    var temporaryPath = FilePath + ".tmp";

    try
    {
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, FilePath, true);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        throw new SettingsException(
            $"The settings file '{FilePath}' could not be written: {exception.Message}", exception);
    }
}
```

Then delete the `UnauthorizedAccessException or IOException` arm from `StartupFailure.Describe`
entirely: with `Write` wrapping, nothing reaches `Describe` under those raw types for a settings
failure any more, so the arm's only remaining job was catching *unrelated* I/O failures under the
wrong label. `_ => (StartupErrorTitle, StartupErrorMessage)` already exists and is exactly what
`startup-diagnostics`'s "A resource the tray icon needs is missing" scenario calls for — no new
fallback logic is needed, and the comment above the deleted arm goes with it.

## Why this over matching origin

The issue's second suggested option — have `Describe` check where the exception came from rather
than its runtime type — is rejected, but not because no such signal ever exists. The codebase already
disambiguates an ambiguous exception type this way twice: `GeminiProvider.SendWithRetryAsync` catches
`HttpRequestException or TaskCanceledException` only `when (!cancellationToken.IsCancellationRequested)`,
telling the client's own request timeout apart from a caller-driven cancellation that happens to raise
the same type; `DictationOrchestrator.RunAsync` catches `OperationCanceledException` only
`when (_shutdown.IsCancellationRequested)`, telling a deliberate quit apart from the transcription
budget expiring. Both work because a `CancellationToken` carries a state flag —
`IsCancellationRequested` — that says whose cancellation this is, checkable at the catch site with no
wrapping needed.

`IOException`/`UnauthorizedAccessException` have no equivalent: a failed `File.Move` carries no bit
saying "this was a settings write," and the deciding code (`StartupFailure.Describe`, reached through
`Program.Main`'s top-level catch) sits far from the throw site — unlike the two examples above, which
catch immediately at the one call that can raise the ambiguous type. Wrapping at the source is the
substitute for that missing flag, and it is also the convention `Read()` already set for this exact
class of failure (settings I/O). `Describe` stays a pure type switch, consistent with every other arm.

## Platform

No platform split. `SettingsStore`, `StartupFailure` and their tests are all in
`Pisum.Whisper.Core`, which is P/Invoke-free; `File.WriteAllText`/`File.Move` raise the same
exception shapes on Windows and macOS for this purpose (`UnauthorizedAccessException` on a
permission-denied target on both).

## Rejected alternatives

- **Have `Describe` inspect the exception's origin instead of its type, the way the two
  cancellation-token cases above do.** Rejected: those work because a `CancellationToken` carries a
  state flag naming whose cancellation it is; `IOException` carries no equivalent, so the only way to
  "inspect origin" here is matching `StackTrace` by substring — exactly the message-text matching
  `DictationFailure` and `StartupFailure` both deliberately avoid ("Failures are described, never
  matched.").
- **Keep the broad `IOException` arm and add a narrower `FileNotFoundException` exclusion.**
  Rejected: treats the symptom (one subclass) rather than the cause (any unrelated `IOException`
  reaching `Describe` unwrapped) — the next unrelated `IOException` would reproduce the bug again.
- **Catch in `Program.Main` at the `SettingsStore.Load()` call site instead of inside `Write`.**
  Rejected: `Save()` — called from `SettingsEditor` at runtime, not only at startup — hits the same
  `Write` and deserves the same wrapping; wrapping once inside `Write` covers both callers with one
  change.

## Tests

- `tests/Pisum.Whisper.Core.Tests/Settings/SettingsStoreTests.cs`: a new case constructs a
  `SettingsStore` over a path inside a directory that does not exist, so `File.WriteAllText` throws
  `DirectoryNotFoundException` (an `IOException`) from `Load()`'s first-launch `Write`; asserts
  `SettingsException`, naming the path, wrapping the original exception.
- `tests/Pisum.Whisper.Core.Tests/Diagnostics/StartupFailureTests.cs`:
  `AnUnwritableSettingsFileIsASettingsError` is rebuilt around a `SettingsException` — mirroring the
  wrap `Write` now performs, in the manner `AParseFailureInsideAnApiKeyDisclosesNoPartOfIt` already
  mirrors `Read`'s — rather than a raw `UnauthorizedAccessException`/`IOException`. A new case passes
  a raw, unwrapped `FileNotFoundException` (as `AssetLoader.Open` raises) and asserts the title is
  "Startup Error" and the message contains neither "settings" nor the settings file name.
