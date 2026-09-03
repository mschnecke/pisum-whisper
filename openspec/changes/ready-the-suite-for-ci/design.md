## Context

See `proposal.md` — *Why*. What follows is measured on Apple Silicon (macOS 26.6.2, SDK `10.0.400`)
on 2026-09-03, against `main` at `d8a596d`.

Thirty-eight full runs of the CI command — thirty-two at normal load, six under deliberate CPU
starvation — and what is in them:

```
 629 tests
  |
  +--   4  Manual (mic / clipboard / keyboard)   --> filtered by --filter-not-trait
  |
  +--   5  WindowsAutostartTests                 --> SKIPPED, with a reason
  |          [Fact(Skip = "The Windows registry is not reachable on this operating system",
  |                 SkipUnless = nameof(WindowsOnly.Enabled), SkipType = typeof(WindowsOnly))]
  |
  +--   5  PresetsViewModelTests (4)             --> FAIL, 12 runs of 12
  |        SettingsEditorTests (1)
  |
  +--   4  ToastPresenterTests                   --> verdict depends on what ran first
  |
  +--   1  SettingsStorePresetRaceTests          --> FAIL 2 of 38; diagnosed, see D1c
  |
  +-- 610  everything else                       --> pass
```

**Two of the three groups are the same category of problem as the skipping one** — a test that
cannot stand on its own where it is being run — handled to three different standards: one skips with
a reason, one fails outright, one passes or fails according to its neighbours.

Per-test measurement, all on this machine, all on `main` at `d8a596d`:

| Test | suite, 32 runs | suite, 6 starved | class alone | test alone |
|---|---|---|---|---|
| the five write-failure tests | **32/32 fail** | **6/6 fail** | fail | fail |
| `ToastPresenterTests.PresentCompletes…FromANonUiThread` | **5/32 fail** | **4/6 fail** | **10/10 fail** | fail |
| `ToastPresenterTests.ThreeStackAndAFourthClosesTheOldest` | **1/32 fail** | 0/6 | pass | not measured |
| `ToastPresenterTests.PresentReturnsBeforeTheWindowExists` | 0/32 | 0/6 | pass | **fail** |
| `ToastPresenterTests.PresentAfterTheUiThreadHasADispatcherShows` | 0/32 | 0/6 | pass | **fail** |
| `SettingsStorePresetRaceTests.APresetIsDeleted…Repeatedly` | **1/32 fail** | **1/6 fail** | 0/60 | 0/60 |
| `FileLoggingRotationTests.WritesDoNotStall…` | **0/32** | **0/6** | — | — |

**Starvation is the instrument.** Fourteen busy loops on a ten-core machine turn two of these rows
from rare into routine, and change nothing about the rest. Both are thread-pool scheduling failures,
and a shared CI runner is a starved machine by construction — so the starved column is the better
predictor of what a pull request will see.

**The last row is why this design was rewritten.** An earlier draft treated the rotation latency
test as the thing that would make CI red, on the strength of `migrate-tests-to-xunit-v3`'s note
calling it "a known flake going into change 12's CI". That measurement was taken on **Windows**. It
was quoted into a macOS-facing design without being re-run, and it does not reproduce here: zero
failures in thirty-eight runs, six of them starved. The gate in D2 is kept because the Windows
evidence still stands, but it is not the macOS problem and this design no longer says it is.

`CLAUDE.md` states 620 tests and a `223 / 393 / 4` class split. Neither is current;
`surface-settings-save-failures` and others have landed since.

### Why the five fail, exactly

`SettingsStore.Write` is two file operations inside one `try`:

```csharp
File.WriteAllText(temporaryPath, json);
File.Move(temporaryPath, FilePath, true);
```

The tests arrange a failure with `using (File.Open(Store.FilePath, FileMode.Open, FileAccess.Read,
FileShare.None))`.

| | `File.Move(src, dst, overwrite: true)` | consults the destination's open handles? |
|---|---|---|
| Windows | `MoveFileEx` + `MOVEFILE_REPLACE_EXISTING` | **yes** — a `FileShare.None` handle blocks it |
| macOS | `rename(2)` | **no** — POSIX rename checks the *parent directory's* write bit and nothing about the destination file |

So on macOS the move succeeds, `Save` publishes, no notification is raised, and
`notifications.Forced.ShouldHaveSingleItem()` finds zero. **The shipped feature is correct on both
platforms; only the simulation is Windows-shaped.**

The corollary generalises: *any* technique that decorates the destination **file** is Windows-only by
construction. Only techniques that break the **path** or the **parent directory** are portable.

## Goals / Non-Goals

**Goals:**

- `dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual` is green, unattended, on both
  platforms — and green **repeatedly**, not green once.
- Every repaired test passes **on its own** as well as in the suite, so a green result is evidence
  rather than a side effect of ordering.
- Every test that does not run somewhere says so **in the runner output, with a reason**, rather
  than being filtered away by a flag in a workflow file.
- `add-packaging-ci`'s CI invocation needs exactly one filter.

**Non-Goals:**

- Deciding what the rotation latency bound should be. That is `file-logging`'s.
- Changing `ToastPresenter`'s readiness gate. It is correct and it fixed a real startup failure; the
  four tests around it are what is wrong.
- Moving `tests/Pisum.Whisper.App.Tests` off `PerTest` isolation. D1b is evidence that `PerTest`
  isolates less than `CLAUDE.md` claims, and that is worth knowing, but changing the isolation mode
  of a whole assembly to fix four tests is the larger change and would need its own argument.
- Auditing the remaining 611 tests for order dependence. The twelve runs plus the alone-runs of the
  repaired tests are the evidence, not a per-test review.

## Decisions

### D1 — The five arrange their failure with a directory at the destination path

Six candidate techniques were probed, each replicating `Write`'s two lines verbatim. Measured on
macOS; the Windows column is reasoned from the API each one reaches, and is the open question below.

| | Technique | macOS (measured) | Windows (reasoned) | Portable |
|---|---|---|---|---|
| T0 | `FileShare.None` on the destination — *current* | **no throw** | throws | no |
| **T1** | **a directory at `Store.FilePath`** | **`IOException` "Is a directory"** | `MoveFileEx` over a directory fails | **yes** |
| T2 | the parent directory does not exist | `DirectoryNotFoundException` | identical | yes |
| T3 | a directory at `FilePath + ".tmp"` | `UnauthorizedAccessException` | identical | yes |
| T8 | a path element is a regular file | `DirectoryNotFoundException` | identical | yes |
| T5 | `FileAttributes.ReadOnly` on the destination | **no throw** | throws | no |
| T4 | parent stripped of its write bit | `UnauthorizedAccessException` | `PlatformNotSupportedException` | no |

`Write` catches `when (exception is IOException or UnauthorizedAccessException)`, and
`DirectoryNotFoundException` derives from `IOException`, so every portable row lands in the filter.

**T1 is chosen because of `SettingsEditorTestBase`.** It creates one flat temporary directory in its
constructor and deletes it recursively in `Dispose`:

```
<temp>/pisum-whisper-tests/<guid>/          <-- created in ctor, deleted in Dispose
    .pisum-whisper.json                     <-- Store.FilePath, directly inside it
```

The store's file sits *directly* in the directory `Dispose` owns — which is what the current test
comment is defending ("without deleting the test's own temp directory, which `Dispose` still
needs"). T2 and T8 need that file one level deeper, so adopting either means changing the base class
and therefore all 39 tests in the two classes rather than the 5 that are broken. T1 and T3 *add* a
directory instead of removing one, so `Dispose`'s recursive delete already cleans up after them.

Between the two, T3 reaches into `Write`'s private `.tmp` naming convention; T1 uses only
`Store.FilePath`, which the tests already know. The arrangement becomes:

```csharp
// A directory where the settings file belongs: File.Move cannot replace it on either
// platform. The FileShare.None lock this replaces only failed on Windows — POSIX
// rename(2) ignores the destination's open descriptors entirely.
File.Delete(Store.FilePath);
Directory.CreateDirectory(Store.FilePath);
```

*T2 is the more faithful simulation and is recorded as the alternative*, because `CLAUDE.md` names
the failures this feature exists for — "disk full, permission denied, a network drive gone from
under the home directory" — and T2 **is** the third of those, where T1 simulates a state no user
will ever be in. It is not chosen only because of the base-class cost. If the base is ever nested
for another reason, T2 is the better arrangement.

### D1b — The four `ToastPresenterTests` name the dispatcher thread they mean

`settle-win-x64-verification-debt`'s task 4.3 added a readiness gate to `ToastPresenter.Present`,
correctly — raising a notification from a pooled thread before Avalonia is initialised permanently
binds the process's UI thread to a thread that then returns to the pool, which is the startup
failure 11/7.4 exists to prevent:

```csharp
var dispatcher = Dispatcher.FromThread(_uiThread);
if (dispatcher is null)
{
    _logger.LogWarning("A notification was raised before the UI was ready ...", title);
    return;                                   // <-- LiveCount stays 0
}
dispatcher.Post(() => Show(title, message));
```

Four `[AvaloniaFact]` tests construct the presenter with the two-argument constructor, which
captures `Thread.CurrentThread`, and then assert `LiveCount` —
`PresentReturnsBeforeTheWindowExists`, `PresentCompletesWhenCalledFromANonUiThread`,
`ThreeStackAndAFourthClosesTheOldest` and `PresentAfterTheUiThreadHasADispatcherShows`. When
`Dispatcher.FromThread` returns null for that captured thread the gate drops the notification and
the assertion reads `should be 1 but was 0`. Whether it returns null depends on what has already run
in the session — see the table in *Context*.

**These are not flaky tests; they are order-dependent ones, and a passing run is contamination.**
The distinction matters for what gets fixed. A flake is a test that sometimes loses a race with
itself. These lose a race with the *scheduler*: `PresentCompletes…FromANonUiThread` fails 10 times
in 10 when its class runs, and 5 times in 32 when the whole suite does, because more neighbours
mean a better chance one of them registered a dispatcher first — and 4 times in 6 when the pool is
starved, because a contended scheduler undoes that luck.

The arrangement is what changes, not `ToastPresenter`. The type already carries the constructor for
it — `internal ToastPresenter(TimeSpan dwell, ILogger logger, Thread uiThread)`, whose summary says
it exists "so a test can name one that owns no dispatcher".
`PresentBeforeTheUiThreadHasADispatcherIsDroppedAndLogged` uses it to name a thread deliberately
without one, and is the only test in the class that passes alone. The four that fail should name
the thread that *does* own the dispatcher, rather than assuming the thread they happen to be
constructed on is it.

**Which thread that is has not been established, and task 3.1 establishes it before anything is
written.** The mechanism is genuinely unknown: why `Dispatcher.FromThread` answers differently for a
freshly-captured `Thread.CurrentThread` under `[AvaloniaFact]` depending on session history, and why
a pool-thread caller fares worse than a UI-thread one, are both unmeasured. Writing a fix before
that is known would be guessing, and the guess would pass — which is exactly how this arrived.

**Production is very probably unaffected, and "probably" is doing real work in that sentence.**
`Program.Main` constructs the presenter on the main thread before `StartWithClassicDesktopLifetime`,
so the null branch is precisely the pre-dispatcher window the gate was built for, and the
`AutostartReconciler` path that motivated 4.3 runs before Avalonia either way. Nothing here
contradicts that. But the only evidence that the gate behaves correctly *after* startup is four
tests that pass for the wrong reason, so there is currently no evidence either way.

### D1c — The preset race test waits for its reader to start

Reproduced and diagnosed 2026-09-03. It is **not** a product defect, which is the opposite of what
an earlier draft of this design guessed.

```
  the test alone, 60 runs                              0 hits
  Core.Tests assembly alone, 25 runs                   0 hits
  full suite, normal load, 32 runs                     1 hit
  full suite, 14 busy loops on a 10-core box, 6 runs   1 hit
```

The captured failure is the last assertion in the test, and the line above it is what settles the
question:

```csharp
await stop.CancelAsync();
await Should.NotThrowAsync(reader);      // <-- PASSED

// So a reader that never actually ran cannot pass this test by doing nothing.
reads.ShouldBeGreaterThan(0);            // <-- failed: reads was 0
```

`Should.NotThrowAsync(reader)` passed, so the reader never threw and
`SettingsStore.DeletePreset`'s clone-and-replace survived exactly the concurrent read it exists to
survive. What failed is the test's guard against measuring nothing, and it fired correctly:
`Task.Run` queues the reader, the thirty `DeletePreset` calls run to completion, `CancelAsync`
fires, and on a starved pool the reader reaches its first iteration only after all of that — so
`reads` is 0.

The window the reader needs is however long thirty writes take. On an idle machine that is plenty;
on a contended one it is sometimes nothing, which is why 60 isolated runs could not produce it and
six starved ones did.

**The fix is to remove the hope, not the guard.** The reader signals its first iteration through a
`TaskCompletionSource`, and the test awaits that — bounded, so a genuinely dead reader still fails
rather than hanging — before entering the deletion loop. `reads > 0` then holds by construction and
the assertion goes back to meaning what it says. Nothing in `SettingsStore` changes.

*Alternative rejected:* deleting the `reads.ShouldBeGreaterThan(0)` guard, which is the one line
standing between this test and a vacuous pass. Its comment says so, and the comment is right.

**The starvation recipe is the useful residue.** Fourteen busy loops on a ten-core machine moved
this from 1-in-32 to 1-in-6, and moved `PresentCompletes…FromANonUiThread` from 5-in-32 to 4-in-6.
Both are thread-pool scheduling failures, and a shared CI runner is a starved machine by
construction — so the recipe is how this class of defect is reproduced on demand rather than waited
for, and task 5.1 uses it.

### D2 — The latency test gates itself on `PISUM_WHISPER_RUN_TIMING`

A new `TimingTests` gate in `tests/Pisum.Whisper.Core.Tests/`, beside `ManualTests.cs`, in exactly
its shape and `WindowsOnly`'s:

```csharp
internal static class TimingTests
{
    public static bool Enabled =>
        Environment.GetEnvironmentVariable("PISUM_WHISPER_RUN_TIMING") is not (null or "");
}
```

applied to `FileLoggingRotationTests.WritesDoNotStallTheCallingThreadWhenTheFileRolls` through
`Skip` / `SkipUnless` / `SkipType`, the way the four manual tests and the five
`WindowsAutostartTests` already are.

**The class stays in its `DisableParallelization` collection.** That is what
`migrate-tests-to-xunit-v3` added for the *other* tests in the same class, which still run.

**This is not a fix and does not pretend to be one.** The bound is `file-logging`'s to set, and the
test stays runnable by anyone who opts in on a quiet machine — which is the only place a p99.9 of
500 µs means anything.

**The gate is kept on Windows evidence alone, and this design says so rather than implying more.**
`migrate-tests-to-xunit-v3` measured roughly three failures in twenty-two runs on the developer
machine, which is Windows; twelve runs here produced none. Two readings survive that: the bound is
comfortable on Apple Silicon and tight on the other machine, or the flake needs a load this Mac did
not reproduce. Nothing distinguishes them without running it on win-x64, which task 4.2 does — and
if it never fails there either, the honest follow-up is to remove the gate rather than keep a skip
nobody needs. Gating first is the conservative order: a skipped test that turns out not to need
skipping costs one line; an unskipped flake costs a red check on somebody else's pull request.

### D3 — A skip, not a CI filter, and that is the point

`add-packaging-ci`'s design D9 excluded this test with `--filter-not-method` in the workflow. Two
things were wrong with it, and this change deletes it rather than inheriting it.

It **contradicts that change's own spec**, which requires the run to "exclude only those tests that
require a person at the machine" — and this test requires no person, only a quiet machine.

And a name in a workflow file is invisible where it matters. A skip prints its reason in the runner
output next to the test; a `--filter-not-method` prints nothing at all, and the count silently drops
by one. The repository already made this exact judgement twice, in `ManualTests`' own summary — the
gate exists *because* "xUnit has no way to run a test that is skipped unconditionally", so that the
tests "stay skipped, with their reason in the runner output".

So the rule this change establishes, and which `add-packaging-ci`'s spec is reworded to require:
**every test that can run unattended on a platform runs there; one that cannot declares itself
skipped with a reason.** The CI invocation then needs exactly one filter, the Manual trait, and it
is the only one it will ever need.

**The rule has a second edge, which D1b is the case for.** A test that passes only because of what
ran before it is not covered by "runs there" in any useful sense — it runs, and its result means
nothing. So the verification for this change is deliberately *two* runs, not one: the whole suite,
and each repaired test **on its own**. Task 4.3 is that second run, and it is the check that would
have caught the `ToastPresenterTests` regression when the gate was added, since the full-suite run
that ticked those boxes was green three times in four.

### D4 — `SettingsStore` is not touched

Not the `Write` implementation, and no injected file-system seam. The five failures are a defect in
how a failure is *simulated*, and `SettingsStore` is deliberately a concrete class following the
`SettingsStore`-not-`ISettingsStore` precedent. Adding an abstraction to make a two-line method
mockable would be a larger change to shipped code than to the tests that are actually wrong.

### Rejected alternatives

- **T0 kept, with the five gated on `WindowsOnly.Enabled`** — a two-line diff and it matches
  `WindowsAutostartTests` exactly, but it leaves a platform-independent `catch`-and-notify path
  covered on one platform only, for no reason other than the arrangement being awkward. Rejected
  because a portable arrangement exists and is one line.
- **T2 / T8** — more faithful; costs a base-class change touching 39 tests. See D1.
- **T3** — portable and needs no base change, but couples the test to `Write`'s private `.tmp` name.
- **T5 `FileAttributes.ReadOnly`** — measured to not throw on macOS, for the same reason T0 does not.
- **T4 `File.SetUnixFileMode`** — `PlatformNotSupportedException` on Windows, so it needs a branch,
  and mode bits are ignored under root.
- **A file-system seam in `SettingsStore`** — see D4.
- **Deleting the latency test** — it is the only statement of an invariant `file-logging`'s spec
  never makes; deleting it would lose the invariant along with the flake.
- **Relaxing the latency bound to something a CI runner passes** — picks a number to fit the noisiest
  machine that will ever run it, which is how a bound stops meaning anything.
- **Excluding by `--filter-not-method` in CI** — `add-packaging-ci`'s D9. See D3.
- **Deleting the four `ToastPresenterTests`** — they cover a gate that prevents a real startup
  failure, and the two that pass beside siblings do assert something true when the dispatcher is
  found. The arrangement is wrong, not the intent.
- **Relaxing `ToastPresenter.Present` so the gate cannot drop a notification in tests** — changing
  shipped behaviour to make a test pass, and the behaviour it would change is the one 11/7.4 was
  written to protect.
- **Ordering the `ToastPresenterTests` so a dispatcher-warming test always runs first** — makes the
  contamination deliberate instead of accidental, and leaves every one of them still unable to run
  alone.
- **Moving `App.Tests` to `PerAssembly` isolation** — the contamination would become the documented
  arrangement, and `PerAssembly` is documented as unsafe for exactly this assembly's kind of global
  state. A larger change than the problem.

## Risks / Trade-offs

- **T1's Windows behaviour is reasoned, not measured.** → It hinges on whether `MoveFileEx` returns
  `ERROR_ACCESS_DENIED` or `ERROR_ALREADY_EXISTS`; .NET maps those to `UnauthorizedAccessException`
  and `IOException` respectively, and `Write`'s filter catches both, so the test survives either
  way. The probe is re-runnable and task 1.1 runs it on Windows before the change lands.
- **The five tests get weaker at naming what they simulate.** → A locked file reads as a plausible
  accident; a directory where a file belongs does not. Mitigated by the comment in D1, which says
  what it stands in for and why the previous arrangement was replaced.
- **`PISUM_WHISPER_RUN_TIMING` means the latency test will, realistically, never run.** → True of
  the four manual tests too, and it is the honest state: nobody was running it deliberately before,
  they were tolerating it failing. A named gate at least makes the omission visible.
- **This change makes CI green without making the suite stronger.** → Accepted, and it is why the
  bound and the missing `file-logging` requirement are recorded here rather than quietly closed.
- **D1b's mechanism is unknown, so its fix is unknown too.** → Task 3.1 measures before task 3.2
  writes, and the task list says explicitly that a fix written first would pass for the same wrong
  reason the tests currently do. If 3.1 cannot establish which thread owns the dispatcher, the
  fallback is the `WindowsOnly`-shaped one: gate the three on a condition that is *checked*, so they
  skip with a reason instead of passing by luck.
- **The `SettingsStorePresetRaceTests` failure is diagnosed and is not a product defect.** → See
  D1c. `SettingsStore.DeletePreset` held; the test's own anti-vacuous-pass guard fired because its
  reader task never got scheduled.
- **Twelve runs is a thin sample.** → It is enough to overturn the earlier draft, which rested on
  zero runs on this platform, and not enough to bound a rate. Where the design says "3 in 12" it
  means the observation, not a probability.

## Migration Plan

1. Re-run the probe on Windows (task 1.1). If T1 does not throw there, fall back to T3, which needs
   the same one-line arrangement and no base-class change.
2. Establish the dispatcher-thread mechanism (task 3.1) before writing anything for D1b. This is the
   one place in the change where measuring first is load-bearing rather than tidy.
3. The five tests, the four `ToastPresenterTests`, the preset race test and the timing gate land
   together in one pull request, verified two ways on both platforms: the whole suite ten times,
   **including runs under D1c's starvation recipe**, and each repaired test run on its own.
4. `add-packaging-ci`'s D9, its spec requirement and its task 5.2 are amended in the same pull
   request, so its committed design never describes a filter that no longer exists.

**Rollback** is reverting one pull request; no shipped code changes, so nothing to un-deploy.

## Open Questions

- **Does T1 throw on Windows?** The only genuinely unmeasured thing here, and task 1.1 answers it
  before anything else is written. Both plausible error codes map to types `Write` catches, so the
  answer changes the exception type in the runner output and nothing else.
- **Should `file-logging` gain a requirement that logging does not stall the calling thread?** Its
  spec has six requirements and none of them says this, so the 500 µs test currently guards an
  invariant that is written down nowhere. Out of scope here — but it is the reason D2 gates the test
  rather than deleting it.
- **Why does `Dispatcher.FromThread` answer differently depending on session history?** D1b's whole
  mechanism. Task 3.1 answers it, and unlike the two questions above this one *must* be answered
  before the change can be written, so it is a task rather than a deferral.
- **Does the rotation latency test still flake on Windows?** Twelve runs here say it does not flake
  on macOS; `migrate-tests-to-xunit-v3`'s note says it does there. Task 4.2 settles it, and a
  negative answer means D2's gate should be removed rather than kept.
- **Do the four `ToastPresenterTests` fail alone on Windows too?** The whole of D1b is measured on
  one Mac. If the ordering dependence is macOS-only the fix is unchanged, but the story of how it
  went unnoticed is not — it would mean the win-x64 run that ticked 4.2 and 4.3 could not have seen
  it at all.
