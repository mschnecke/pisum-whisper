# Ready the test suite for continuous integration

## Why

`add-packaging-ci` adds the first CI this repository has had, and its macOS leg is red before a line
of packaging is written. Measured on Apple Silicon, 2026-09-03, on `main`:

```
dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual
  625 selected | 615 pass | 5 FAIL | 5 skip      (a second run: 6 FAIL)
```

The suite has run on macOS exactly twice: by hand in issue #31's sitting, which is how the five
were found, and once for this proposal. Change 12 makes it routine, and a first CI run red for
reasons unrelated to CI teaches everyone to ignore the check.

## What Changes

- **Five write-failure tests become portable.** `PresetsViewModelTests` (4) and `SettingsEditorTests`
  (1) fail `SettingsStore.Write` by holding the settings file open with `FileShare.None`.
  That is Windows-only: `File.Move(tmp, path, overwrite: true)` is `MoveFileEx`, which consults the
  destination's sharing mode, and `rename(2)` on macOS, which ignores open descriptors entirely — so
  the write succeeds and the "should have failed" assertions fail instead. A directory at the
  destination replaces it; neither platform renames over one. Reported in PR #48 and left for an
  owner the archive lacks.
- **The rotation latency test gates itself rather than being filtered by CI.**
  `FileLoggingRotationTests.WritesDoNotStallTheCallingThreadWhenTheFileRolls` asserts a p99.9 write
  latency under 500 µs, measuring the machine as much as the code — `migrate-tests-to-xunit-v3`
  recorded it failing three times in twenty-two runs on a developer machine, and a shared runner is
  noisier. It gains an environment-variable gate shaped like `ManualTests.Enabled`, reporting
  **skipped with its reason** rather than vanishing behind a `--filter-not-method` nobody reads.
- **`CLAUDE.md`'s test counts are corrected**: it states 620 and `223 / 393 / 4`; the suite is 629.

`skip_specs: true`, the precedent `migrate-tests-to-xunit-v3` set: no shipped behaviour changes,
and `file-logging`'s six requirements never mention write latency, so nothing under
`openspec/specs/` is affected. The reference has no counterpart — its CI runs no tests.

## Impact

**Blocks `add-packaging-ci`'s task group 5.** Its `ci.yml` cannot land green without this, and its
design D9 is deleted rather than shipped.

## Non-goals

- No change to `SettingsStore.Write`. The five failures are a test technique, not a defect in it.
- No new latency bound, and no deletion of the latency test. The p99.9 belongs to `file-logging`.
- No file-system seam injected into `SettingsStore` to make the write mockable.
- No audit of the other 614 tests.
