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
  +--   5  ToastPresenterTests                   --> verdict depends on what ran first
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
| `ToastPresenterTests.ANotificationGoesAwayOnItsOwn` | 0/32 | 0/6 | pass | **fail** |
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
- Changing `ToastPresenter`'s readiness gate. It fixed a real startup failure, and D1b's
  investigation cleared it of any part in these five failures.
- Moving `tests/Pisum.Whisper.App.Tests` off `PerTest` isolation. `PerTest` gives each test a fresh
  dispatcher and therefore a fresh first-window cost, which is what makes D1b's failure reproducible
  alone; changing a whole assembly's isolation mode to fix five tests is the larger change and would
  need its own argument.
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

### D1b — The five `ToastPresenterTests` stop racing their own dwell

Investigated 2026-09-03, then **re-investigated during apply and rewritten**. Two earlier drafts of
this decision were wrong, and the corrections are the substance of this section.

**Refuted (first draft) — the readiness gate is innocent.** `settle-win-x64-verification-debt`'s
`Dispatcher.FromThread` gate has no part in this. With a recording logger in place of `NullLogger`,
`Dispatcher.FromThread(_uiThread)` returns non-null and is *reference-equal* to
`Dispatcher.UIThread`, and no warning is logged. `Present` never reaches its early return.

**Refuted (second draft) — the job is not discarded.** The second draft concluded that the job
`Present` posts "is discarded, not deferred", on the strength of three `RunJobs()` calls failing to
recover it, and scoped this task to reading Avalonia's source for *why*. Reading it, and then
measuring against it, shows there is no discard to explain. `Dispatcher.RunJobs` drains and does no
first-call initialisation; `InvokeAsyncImpl` drops an operation only when the dispatcher has already
finished shutting down or `RequestProcessing()` returns false, and in 12.1.1 `RequestProcessing()`
returns `true` on every path. Inspecting the queue through `Dispatcher.GetJobs()` around a real
`Present` settles it directly:

```
before Present  [3 x DispatcherOperation/prio=Inactive/status=Pending]     <-- session setup
after  Present  [3 x Inactive, 1 x DispatcherOperation/prio=Default/status=Pending]
after  RunJobs  [3 x Inactive]                          live=0   <-- dequeued and executed
```

The operation is enqueued, dequeued and run. With `Dispatcher.UIThread.UnhandledException` hooked,
**nothing throws**. So the notification is created — and then taken away again.

**Established — the dwell elapses inside the same `RunJobs()` call.** The first `ToastWindow` of a
headless session costs about 135 ms, and the tests inject a 30 ms dwell:

```
  first  RunJobs of a session   134.9 ms   live=0
  second Present, same session    0.8 ms   live=1
```

`Show` adds to `_live`, shows the window and calls `timer.Start()`; the rest of that 135 ms is still
ahead of it, `ExecuteJob` calls `PromoteTimers()` on the way out, and the drain continues. The
`DispatcherTimer` ticks, `Dismiss` removes the entry, and the test reads `LiveCount` as 0. Varying
only the dwell, cold, locates the threshold between 30 ms and 100 ms:

| dwell | first `RunJobs` | `LiveCount` |
|---|---|---|
| 30 ms | 134.9 ms | **0** |
| 100 ms | 139.5 ms | 1 |
| 200 ms | 135.8 ms | 1 |
| 500 ms | 134.2 ms | 1 |
| 5 min | 129.8 ms | 1 |

**The second draft's premise, stated as a refutation, is what was actually wrong.** It reasoned that
"the 30 ms dwell cannot have elapsed early, because `timer.Start()` is called *inside* `Show`, which
itself only runs during `RunJobs`". Every clause of that is true and the conclusion does not follow:
running *during* `RunJobs` is not protection when `RunJobs` is 135 ms long. Nothing elapsed early.

**Every "cure" in the second draft's list is a warm-up, and that is why it read as a Heisenbug.**
Constructing a `ToastWindow` on the test thread, or merely logging more, pays or defers the
first-window cost. So does the passage of wall-clock time that a debugger or an extra probe buys.
The one apparent counter-example, `Dispatcher.UIThread.RunJobs()` with nothing posted, does **not**
work and was measured wrongly: the three jobs standing in a fresh session's queue are `Inactive`
priority, below `RunJobs`' `MinimumActiveValue`, so a bare pump executes nothing and leaves the queue
exactly as it found it.

**The seven tests fall out of one rule: a notification whose dwell is shorter than the first show is
gone before the assertion.**

| Test | alone | why |
|---|---|---|
| `PresentReturnsBeforeTheWindowExists` | fail | 30 ms dwell, expects 1 |
| `PresentCompletesWhenCalledFromANonUiThread` | fail | 30 ms dwell, expects 1 |
| `PresentAfterTheUiThreadHasADispatcherShows` | fail | 30 ms dwell, expects 1 |
| `ANotificationGoesAwayOnItsOwn` | fail | 30 ms dwell, expects 1 *before* waiting for 0 |
| `ThreeStackAndAFourthClosesTheOldest` | **pass** | presents 4, expects 3 — **the first one's dwell is what makes it 3** |
| `CloseAllRemovesEveryLiveNotification` | **pass** | **five-minute dwell** — the timer cannot fire |
| `PresentBeforeTheUiThreadHasADispatcherIsDroppedAndLogged` | **pass** | a plain `[Fact]`, and it asserts the drop |

The sixth row is the confirmation rather than an exception: it is the only `[AvaloniaFact]` here that
already sets a dwell it cannot race, and it is the only multi-`Present` one that passes alone. Both
earlier drafts had this row in the table and neither read it.

**`ThreeStackAndAFourthClosesTheOldest` does not merely tolerate the defect — it depends on it.** It
presents four and asserts three, and alone it gets three because the first one's timer fires during
the show. Repairing the others breaks it, and task 3.3 expects that.

**`ANotificationGoesAwayOnItsOwn` is the fifth test, and both earlier drafts left it out.** It fails
alone for the same reason, and it is the one test here that is genuinely *about* the dwell, so it
cannot be repaired by lengthening one.

**The fix is to stop racing the dwell.** Four of the five assert that a notification is *up*, and
none of them is a statement about how long it stays: `Present` returns before the window exists, it
works from a pooled thread, a fourth displaces the oldest. Those four take a dwell long enough that
it cannot elapse during the show — five minutes, exactly what `CloseAllRemovesEveryLiveNotification`
already does, named once as a constant rather than repeated. The fifth keeps its 30 ms and instead
constructs and shows one `ToastWindow` first, so the ~135 ms is spent before any timer is running and
the dwell it measures is its own. `ToastPresenter` is not changed and neither is the readiness gate.

*Alternative rejected:* pumping the dispatcher once before each test body — the second draft's fix.
Measured 0 of 4: it executes nothing, for the `Inactive`-priority reason above.

*Alternative rejected:* warming a `ToastWindow` in all five and leaving every dwell at 30 ms. It
passes 4 of 4, but a warmed first `RunJobs` still measures 34 ms against a 30 ms dwell, so it keeps
the race and only shortens the odds. It is used for the fifth test **only**, where the dwell is the
thing under test and there is no alternative.

*Alternative rejected:* naming the dispatcher thread through the existing
`internal ToastPresenter(TimeSpan, ILogger, Thread)` constructor — the first draft's fix, for a cause
that is refuted. The captured thread is already the right one.

**Nothing here is left open.** The second draft closed with one unknown — why Avalonia discards a job
posted to a live, reference-identical dispatcher — and the answer is that it does not discard it.

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

**The rule has a second edge, and D1b is the case for it.** A test that passes only because of what
ran before it is not covered by "runs there" in any useful sense — it runs, and its result means
nothing. So the verification for this change is deliberately *two* runs, not one: the whole suite,
and each repaired test **on its own**. Task 5.3 is that second run, and it is the check that would
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
- **Deleting the five `ToastPresenterTests`** — they cover a gate that prevents a real startup
  failure, and each of them asserts something true about `ToastPresenter`. The arrangement is wrong,
  not the intent.
- **Relaxing `ToastPresenter.Present` so the gate cannot drop a notification in tests** — changing
  shipped behaviour to make a test pass, and the behaviour it would change is the one 11/7.4 was
  written to protect. It would also fix nothing: D1b measured the gate not firing.
- **Naming the dispatcher thread through the three-argument `ToastPresenter` constructor** — the
  first draft's fix, for a cause that has since been refuted. The captured thread is already the
  one that owns the dispatcher.
- **Pumping the dispatcher once before each test body** — the second draft's fix, measured 0 of 4.
  A fresh session's queue holds three `Inactive`-priority jobs, which are below `RunJobs`'
  `MinimumActiveValue`, so the pump executes nothing and the first-window cost is still ahead.
- **Shortening the first window instead of lengthening the dwell** — warming a `ToastWindow` in all
  five and leaving every dwell at 30 ms passes 4 of 4, but a warmed first `RunJobs` still measures
  34 ms against a 30 ms dwell. It keeps the race and only shortens the odds, so it is used for
  `ANotificationGoesAwayOnItsOwn` alone, where the dwell is the thing under test.
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
- **D1b was wrong twice before it was right, and both wrong drafts were written from measurements.**
  → The first blamed the readiness gate, the second concluded the job was discarded; each was
  refuted by measuring the next thing down. What broke the loop was inspecting the dispatcher queue
  and the clock together rather than either alone. The residue is a rule rather than a reassurance:
  in this suite, `LiveCount == 0` has two causes that look identical from outside, and only a
  stopwatch tells them apart.
- **The defect looks cured by observing it.** → Constructing a window or merely logging more makes
  it vanish, because both pay or defer the ~135 ms first-window cost, so a fix that changes timing
  will *appear* to work whether or not it addresses anything. The chosen fix does not change timing:
  it removes the race by making the dwell unreachable. Task 5.3 still verifies each repaired test
  **alone**, which is the only configuration in which the failure is deterministic.
- **The `SettingsStorePresetRaceTests` failure is diagnosed and is not a product defect.** → See
  D1c. `SettingsStore.DeletePreset` held; the test's own anti-vacuous-pass guard fired because its
  reader task never got scheduled.
- **Twelve runs is a thin sample.** → It is enough to overturn the earlier draft, which rested on
  zero runs on this platform, and not enough to bound a rate. Where the design says "3 in 12" it
  means the observation, not a probability.

## Migration Plan

1. Re-run the probe on Windows (task 1.1). If T1 does not throw there, fall back to T3, which needs
   the same one-line arrangement and no base-class change.
2. Settle D1b's mechanism against Avalonia 12.1.1's dispatcher source and a queue-and-clock probe
   (task 3.1b), because the second draft's fix does not survive it. This **does** gate task 3.2.
3. The five tests, the five `ToastPresenterTests`, the preset race test and the timing gate land
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
- **Does the rotation latency test still flake on Windows?** Twelve runs here say it does not flake
  on macOS; `migrate-tests-to-xunit-v3`'s note says it does there. Task 4.2 settles it, and a
  negative answer means D2's gate should be removed rather than kept.
- **Do the five `ToastPresenterTests` fail alone on Windows too?** The whole of D1b is measured on
  one Mac, and what it turns on — the first `ToastWindow` of a headless session costing more than a
  30 ms dwell — is a per-machine number. A faster or slower first show changes how often it is seen,
  never whether the race is there, so the fix is unchanged either way. What would change is the
  story of how it went unnoticed: it would mean the win-x64 run that ticked 4.2 and 4.3 could not
  have seen it at all.
