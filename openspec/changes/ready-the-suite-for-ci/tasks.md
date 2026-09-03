## 1. Settle the technique

- [ ] 1.1 Re-run the failure-injection probe on win-x64 and record which exception each technique produces. The probe is a ~40-line console project replicating `SettingsStore.Write`'s two lines verbatim — `File.WriteAllText(path + ".tmp", json)` then `File.Move(tmp, path, true)` — inside the same `catch (Exception e) when (e is IOException or UnauthorizedAccessException)`. It needs no reference to this repository and is rewritten rather than preserved; the macOS results it produced are in design D1. Verify: T1 (a directory at the destination) throws either `IOException` or `UnauthorizedAccessException` — both are caught by `Write`'s filter, so either answer confirms it. **If T1 does not throw on Windows, stop and take T3** (a directory at `FilePath + ".tmp"`), which needs the same one-line arrangement; design *Migration Plan*, step 1.

## 2. The five write-failure tests

- [ ] 2.1 Replace the `FileShare.None` arrangement in `SettingsEditorTests.ACommitThatCannotBeWritten_NotifiesAndLogsRatherThanThrowingUnobserved` with `File.Delete(Store.FilePath)` + `Directory.CreateDirectory(Store.FilePath)`, carrying the comment from design D1 — what it stands in for, and that the lock it replaces only failed on Windows. Verify: the test passes on macOS, where it fails today.
- [ ] 2.2 Do the same for the four `PresetsViewModelTests` write-failure tests — `Add_`, `Save_`, `Delete_` and `Activate_WhenTheWriteFails_*`. Verify: all four pass on macOS; `dotnet test tests/Pisum.Whisper.App.Tests` reports 39 passed, 0 failed, where it reports 34 / 5 today.
- [ ] 2.3 Confirm the tests still fail for the right reason rather than passing by accident. Verify: temporarily revert `SettingsEditor.Commit()`'s `catch (SettingsException)` and confirm all five fail again — a test that no longer forces a write failure would stay green through that, which is the failure mode this arrangement change could introduce.

## 3. The rotation latency test

- [ ] 3.1 Add `TimingTests` to `tests/Pisum.Whisper.Core.Tests/`, beside `ManualTests.cs` and in its shape: an `internal static` class with `Enabled` reading `PISUM_WHISPER_RUN_TIMING`, and a summary saying what it gates and why a skip is preferred to a runner filter. Verify: it compiles at 0 warnings.
- [ ] 3.2 Apply `Skip` / `SkipUnless` / `SkipType` to `FileLoggingRotationTests.WritesDoNotStallTheCallingThreadWhenTheFileRolls` only, leaving the rest of the class — and its `DisableParallelization` collection — untouched. Verify: an ordinary run reports it skipped **with its reason in the output**, and `dotnet test tests/Pisum.Whisper.Core.Tests --filter-method '*WritesDoNotStallTheCallingThreadWhenTheFileRolls' -e PISUM_WHISPER_RUN_TIMING=1` runs it.

## 4. The full run

- [ ] 4.1 Run the CI command on macOS and record the numbers. Verify: `dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual` is green with **one** filter — 625 selected, 6 skipped (5 `WindowsAutostartTests` + 1 timing), 619 passed, 0 failed — and ten consecutive runs stay green, which is what the timing gate is for.
- [ ] 4.2 Run the same command on win-x64 and record the numbers. Verify: green, 625 selected, 1 skipped (the timing test; `WindowsAutostartTests` runs there), 624 passed, 0 failed.

## 5. Documentation

- [ ] 5.1 Correct `CLAUDE.md`'s *The test stack* section: the counts (620 → 629, and the `223 / 393 / 4` class and test split recomputed), a fourth gate beside `ManualTests` and `WindowsOnly` — `TimingTests` and `PISUM_WHISPER_RUN_TIMING` — and the rule design D3 establishes, that a test which cannot run somewhere declares itself skipped rather than being filtered out by the runner invocation. Also correct the paragraph calling the rotation flake "a known flake going into change 12's CI", which is no longer where it is handled. Verify: the counts match a real run, and `git show --stat` confirms `CLAUDE.md` lands with the tests it describes.
- [x] 5.2 Add the row to `openspec/ROADMAP.md`'s *Off-sequence changes* table. **Done during planning** — a roadmap row is a planning artifact, not implementation. It records this as the first off-sequence change that blocks a numbered one, where the table previously had `report-startup-failures` blocking nothing, and carries the measured 2026-09-03 numbers and the `rename(2)` cause.

## 6. Amend `add-packaging-ci` — done during planning

Its planning artifacts were committed at `6ee1736` describing a CI filter this change deletes.
Amending another change's planning artifacts is planning rather than implementation, so all four
were made in this change's own planning pass rather than left for apply. They are listed because
the record of *what* was amended, and why, belongs with the change that caused it.

- [x] 6.1 Rewrote `add-packaging-ci`'s design D9 — the `--filter-not-method` exclusion is gone, the CI command carries one filter, and D9 now states the rule (a skip explains itself, a runner filter does not) plus the measured per-platform counts. Its *Risks* entry for the rotation flake was rewritten to name this change. Verified: no `--filter-not-method` remains in that change's artifacts except where D9 names it as the thing it rejected.
- [x] 6.2 Reworded `specs/packaging/spec.md`'s requirement *Continuous integration builds and tests both platforms* from enumerating what may be excluded to constraining how, and replaced its third scenario with one covering any test that cannot run on a platform. Verified: `openspec validate add-packaging-ci --strict` passes, and the requirement no longer contradicts D9.
- [x] 6.3 Corrected `add-packaging-ci`'s task 5.2, which claimed "615 tests, the four manual ones report skipped" — wrong on both counts. Verified: it now names 625 selected with the per-platform skip counts, and says it is blocked on this change.
- [x] 6.4 Recorded the dependency in `add-packaging-ci`'s `proposal.md` *Impact*. Verified: it says task group 5 also depends on this change, and `openspec validate add-packaging-ci --strict` still passes.

## Not in this change

- **A `file-logging` requirement that logging does not stall the calling thread.** Its spec has six requirements and none says this, so the 500 µs test guards an invariant written down nowhere. Recorded in design *Open Questions*; it is why the test is gated rather than deleted.
- **A new latency bound.** Choosing a number that a shared runner passes is how a bound stops meaning anything. `file-logging`'s to set.
- **Any change to `SettingsStore`.** Design D4 — the failures are in how a failure is simulated, not in the code being tested.
- **An audit of the remaining 614 tests.** The full run in design *Context* is the evidence, not a per-test review.
