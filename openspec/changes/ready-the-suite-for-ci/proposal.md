# Ready the test suite for continuous integration

## Why

`add-packaging-ci` adds the first CI this repository has had, and its macOS leg is red before a line
of packaging is written. Twelve full runs on Apple Silicon, 2026-09-03, on `main`:

```
dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual
  5 failures every run, and a sixth in three of the twelve
```

Change 12 makes that run routine, and a first CI run red for reasons unrelated to CI teaches
everyone to ignore the check.

## What Changes

- **Five write-failure tests become portable.** `PresetsViewModelTests` (4) and `SettingsEditorTests`
  (1) fail `SettingsStore.Write` by holding the settings file open with `FileShare.None`. That is
  Windows-only: `File.Move(tmp, path, overwrite: true)` is `MoveFileEx`, which consults the
  destination's sharing mode, and `rename(2)` on macOS, which ignores open descriptors — so the
  write succeeds and the "should have failed" assertions fail instead. A directory at the
  destination replaces it; neither platform renames over one. Reported in PR #48, owner long
  archived.
- **Three `ToastPresenterTests` stop depending on what ran before them.** They assert `LiveCount`
  after `Present`, which since `settle-win-x64-verification-debt`'s `Dispatcher.FromThread`
  readiness gate returns early when no dispatcher is registered for the captured thread. Alone, all
  three fail; beside siblings, two pass; in the whole suite, one fails three times in twelve. **A
  green result there is contamination, not evidence** — which is how a correct gate silently
  regressed three tests whose boxes were ticked.
- **The rotation latency test gates itself** on an environment variable shaped like
  `ManualTests.Enabled`, reporting **skipped with its reason** rather than vanishing behind a
  `--filter-not-method` nobody reads. *Its flakiness is `migrate-tests-to-xunit-v3`'s Windows
  measurement; it failed 0 of 12 runs here.* The gate is kept for the platform that saw it.
- **`CLAUDE.md`'s test counts are corrected**: it states 620; the suite is 629.

`skip_specs: true`, the precedent `migrate-tests-to-xunit-v3` set: no shipped behaviour changes.

## Impact

**Blocks `add-packaging-ci`'s task group 5.** Its `ci.yml` cannot land green without this, and its
design D9 is deleted rather than shipped.

## Non-goals

- No change to `SettingsStore.Write` or `ToastPresenter`: both are defects in how a condition is
  arranged, not in the code under test.
- No new latency bound, and no deletion of the latency test. The p99.9 belongs to `file-logging`.
- No move away from `PerTest` isolation, though this is the evidence it isolates less than claimed.
- No audit of the remaining 611 tests.
