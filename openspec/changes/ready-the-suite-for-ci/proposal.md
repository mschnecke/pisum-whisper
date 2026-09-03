# Ready the test suite for continuous integration

## Why

`add-packaging-ci` adds the first CI this repository has had, and its macOS leg is red before a line
of packaging is written. 38 full runs on Apple Silicon, 2026-09-03, on `main`:

```
dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual
  5 failures in all 38 runs, and at least one more in 11 of them
```

Change 12 makes that run routine, and a first CI run red for reasons unrelated to CI teaches
everyone to ignore it.

## What Changes

- **Five write-failure tests become portable.** `PresetsViewModelTests` (4) and `SettingsEditorTests`
  (1) fail `SettingsStore.Write` by holding the settings file open with `FileShare.None`. That is
  Windows-only: `File.Move(tmp, path, overwrite: true)` is `MoveFileEx`, consulting the
  destination's sharing mode, and `rename(2)` on macOS, which ignores open descriptors — so the
  write succeeds and the "should have failed" assertions fail instead. A directory at the
  destination replaces it; neither platform renames over one. Reported in PR #48, owner long
  archived.
- **Four `ToastPresenterTests` stop depending on what ran before them.** They assert `LiveCount`
  after `Present`, and the job `Present` posts is **discarded, not deferred**, on the first
  dispatcher cycle of a fresh headless session. Alone all four fail; beside siblings most pass;
  across 38 suite runs they failed 10 times, four in the six run under CPU starvation. **A green
  result there is contamination, not evidence.** One of them asserts three notifications and passes
  alone *because* the first is eaten.
- **The rotation latency test gates itself** on an environment variable shaped like
  `ManualTests.Enabled`, reporting **skipped with its reason** rather than vanishing behind a
  `--filter-not-method` nobody reads. *Its flakiness is `migrate-tests-to-xunit-v3`'s Windows
  measurement; it failed 0 of 12 runs here.* The gate is kept for the platform that saw it.
- **`CLAUDE.md`'s counts are corrected**: it says 620; the suite is 629.

`skip_specs: true`, the `migrate-tests-to-xunit-v3` precedent: no shipped behaviour changes.

## Impact

**Blocks `add-packaging-ci`'s task group 5.** Its `ci.yml` cannot land green without this, and its
D9 is deleted rather than shipped.

## Non-goals

- No change to `SettingsStore.Write` or `ToastPresenter`: the defects are in how a condition is
  arranged, not in the code under test.
- No new latency bound and no deletion of that test; the p99.9 is `file-logging`'s.
- No move off `PerTest` isolation, though this is evidence it isolates less than claimed.
- No audit of the other 610 tests.
