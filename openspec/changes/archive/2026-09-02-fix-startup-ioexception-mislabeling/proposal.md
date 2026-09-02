## Why

`StartupFailure.Describe` matches on exception *type* to decide whether a startup failure is a
"Settings Error": `SettingsException`, or a bare `UnauthorizedAccessException`/`IOException`. The
second arm exists because `SettingsStore.Write` is an unguarded `File.WriteAllText` + `File.Move`,
called from `Load` on first launch, so an unwritable home directory reaches the catch as a raw
exception rather than a `SettingsException` the way `Read()`'s own failures already do. But
`IOException` is also what any other startup step raises on an unrelated file failure. Issue #34
reproduced this from `Avalonia.Platform.StandardAssetLoader.Open` on a missing tray icon resource — a
`FileNotFoundException`, an `IOException` subclass — which the design already calls a "Startup Error"
(`startup-diagnostics`'s "A resource the tray icon needs is missing" scenario), but the type-based
match sends it to "Settings Error" instead, telling the user their settings file — which holds their
API keys — is broken when it never was.

## What Changes

- `SettingsStore.Write` catches `IOException`/`UnauthorizedAccessException` and rethrows as
  `SettingsException`, exactly as `Read()` already wraps its own failures, so a write failure carries
  the same type whether it happens on first launch (`Load`) or a later `Save`.
- `StartupFailure.Describe` drops the `UnauthorizedAccessException or IOException` arm: with `Write`
  wrapping, nothing reaches it under those raw types for a settings failure any more, so any
  unrelated I/O failure — the tray asset, or anything else — falls through to the existing
  `_ => (StartupErrorTitle, StartupErrorMessage)` arm, which is what the design already specifies.
- Update `StartupFailureTests`: the unwritable-settings-file case is rebuilt as a `SettingsException`;
  a new case asserts a raw, unwrapped `FileNotFoundException` is described as "Startup Error" and
  never names the settings file.
- Add a `SettingsStoreTests` case asserting a write failure is wrapped in `SettingsException` naming
  the path.

Reference: **none.** This is a defect in this codebase's own exception-type matching; the reference
implementation has no equivalent exception hierarchy to consult.

## Capabilities

### New Capabilities
_None._

### Modified Capabilities
- `startup-diagnostics`: a startup failure is identified by where it actually occurred, not by the
  runtime type of the exception it happened to raise.
- `settings-persistence`: a settings file that cannot be written raises the same kind of error
  regardless of whether the failure happens on first launch or a later save.

## Impact

Off-sequence, so no number, per `ROADMAP.md`. Code changes: `SettingsStore.Write`,
`StartupFailure.Describe`, and their tests. No new dependency, no schema change, no UI change.
Closes #34.

## Non-goals

- No change to what a genuine settings failure's message says — `SettingsException`'s own wrapping
  and `Describe`'s `SettingsException` arm are untouched.
- No broader exception-taxonomy rework; only the one type-based match this bug is in.
- No behavior change for `Save`'s runtime callers (`SettingsEditor`, `PresetsViewModel`) beyond the
  exception's type — `Write` already threw on failure, unobserved by either caller today.
