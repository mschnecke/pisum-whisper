## Context

See `proposal.md` — Why. What matters for the approach is the shape of what is being migrated,
measured rather than assumed:

| | `Pisum.Whisper.Core.Tests` | `Pisum.Whisper.Platform.Tests` |
|---|---|---|
| Baseline | 366 passed, 2 skipped, 12 s | 2 passed, 2 skipped, 0.2 s |
| Source files | 58 | 3 |

The MSTest surface in use is small and uniform: `[TestClass]` ×53, `[TestMethod]` ×350,
`[DataRow]` ×28 across 6 methods, `[TestInitialize]` ×14, `[TestCleanup]` ×14, `[Ignore]` ×4.
Across 61 source files, four facts make the migration mechanical rather than a rewrite:

- **There is not one `Assert.*` call.** Every assertion is Shouldly, so the assertion library does
  not move and no assertion semantics change.
- **`TestContext` is never used**, so nothing depends on MSTest's ambient context object.
- **All 14 `[TestInitialize]`/`[TestCleanup]` pairs are synchronous `void`**, so none of them needs
  xUnit's asynchronous lifecycle.
- **No test file names the MSTest namespace.** `Microsoft.VisualStudio.TestTools.UnitTesting` is
  supplied by MSTest's implicit global using (see the generated
  `obj/Debug/net10.0/*.GlobalUsings.g.cs`).

Both projects already set `<OutputType>Exe</OutputType>`, which xUnit v3 requires. The constraint
that shapes several decisions below is `TreatWarningsAsErrors` in `Directory.Build.props`:
warnings-as-errors is, per `CLAUDE.md`, "the whole quality gate today".

## Goals / Non-Goals

**Goals:**

- One `PackageReference` per test project for the whole test framework.
- A diff in the 61 test source files that is attribute renames and lifecycle reshaping only — no
  changed assertion, no changed arrangement, no changed test name except the six theories whose
  display names MSTest spelled differently.
- `dotnet build Pisum.Whisper.slnx` stays at 0 warnings without a single new suppression.
- The same 372 tests, with the same 4 still skipped.

**Non-Goals:**

- Restructuring the four abstract test base classes. They map to constructors and `Dispose` as they
  stand.
- Introducing `IClassFixture`, `ICollectionFixture` or `AssemblyFixture`. Nothing in the suite shares
  state across tests today and this change must not create the first thing that does.
- Renaming test classes or methods to an xUnit naming convention.

## Decisions

### D1 — One package: `xunit.v3` **3.2.2**, the version Avalonia is built against

`Directory.Packages.props` gains `<PackageVersion Include="xunit.v3" Version="3.2.2" />` and loses
`MSTest` and `Microsoft.NET.Test.Sdk`. `SharpHook.Testing`, `FakeItEasy` and `Shouldly` are untouched.

The single reference is sufficient because of what it pulls (verified against the published nuspecs):

```
xunit.v3 3.2.2
└── xunit.v3.mtp-v1 3.2.2
    ├── xunit.v3.core.mtp-v1 3.2.2
    │   ├── xunit.v3.extensibility.core  [3.2.2]   the framework; AssemblyVersion 3.2.2.0
    │   ├── xunit.v3.runner.inproc.console [3.2.2] the in-process MTP runner
    │   └── Microsoft.Testing.Platform 1.9.1       (+ .MSBuild, .Telemetry, TrxReport.Abstractions)
    ├── xunit.v3.assert 3.2.2          unused here; Shouldly stays
    └── xunit.analyzers 1.27.0         see D7
```

`buildTransitive/xunit.v3.core.mtp-v1.targets` raises a hard MSBuild error unless `OutputType` is
`Exe` — already true of both projects, so no property changes.

**The version is chosen by `Avalonia.Headless.XUnit`, not by preference.** Change 10 is the settings
window, and Avalonia ships first-party headless test integration for xUnit and NUnit only — there is
no `Avalonia.Headless.MSTest` and there never has been. `Avalonia.Headless.XUnit` 12.1.1, which
matches this repository's Avalonia pin, has a `net10.0` dependency group naming
`xunit.v3.extensibility.core` **3.2.2**, and its `[AvaloniaFact]` / `[AvaloniaTheory]` hook xUnit's
*extensibility* surface — custom test framework and test-case discovery, the fastest-moving API in
the library.

Taking `xunit.v3` 4.0.0 instead would not fail to restore, which is what makes it dangerous:
`xunit.v3.core.mtp-v2` 4.0.0 pins `xunit.v3.extensibility.core` to exactly `[4.0.0]`, and 4.0.0 also
satisfies Avalonia's `>= 3.2.2` range, so NuGet resolves it silently with no conflict and no warning.
But the assembly version does move across the major — `xunit.v3.core.dll` is `3.2.2.0` in the 3.2.2
package and `4.0.0.0` in the 4.0.0 package — so `Avalonia.Headless.XUnit` would be running against a
major it was not compiled for, and the failure would surface at change 10 rather than here.

The cost of the pin is recorded rather than hidden: 3.2.2 sits on `xunit.v3.mtp-v1` and therefore
**Microsoft.Testing.Platform 1.9.1**, where MSTest 4.3.3 already resolves Microsoft.Testing.Platform
2.3.3 today. This change therefore moves the platform version *backwards*. See D2 and Risks.

### D2 — Microsoft.Testing.Platform, opted into through `global.json`

On SDK 10.0.400, `dotnet test --help` says it plainly: *".NET Test Command for VSTest. To use
Microsoft.Testing.Platform, opt-in to the Microsoft.Testing.Platform-based command via global.json."*
So `global.json` gains a sibling to the existing `sdk` block:

```json
{
  "sdk": { "version": "10.0.400" },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

This is a repository-wide switch, not a per-project one, and it is the reason
`Microsoft.NET.Test.Sdk` can be dropped rather than merely unused: with the MTP command, the test
assembly is executed directly through its own apphost and nothing hosts it.

**One thing here is unverified and must not be assumed.** D1's pin puts the test projects on
Microsoft.Testing.Platform **1.9.1**, while the SDK's MTP-mode `dotnet test` and the repository's
current MSTest are both on the 2.x line. Whether the .NET 10 SDK's MTP command drives a 1.9.x test
app has not been checked, and it is the first thing task 2.3 has to establish — before 61 files are
rewritten against it. If it does not, the options are the VSTest bridge from Rejected alternatives
(which is indifferent to the MTP version) or `xunit.v3` 4.0.0 with the Avalonia question deferred to
change 10; that is a decision to bring back rather than to make silently.

The consequence to plan for is the CLI surface. `dotnet test Pisum.Whisper.slnx` is unchanged, but
`dotnet test --filter <expr>` is VSTest syntax and stops working. Running a single manual test —
which is how `ManualCaptureSmokeTest`, `ManualTranscriptionSmokeTest`, `ManualClipboardRoundTrip` and
`ManualDictationSmokeTest` are meant to be used — becomes an MTP filter, and the working incantation
must be captured in `CLAUDE.md` during the migration rather than left for the next person to
rediscover.

### D3 — The attribute mapping

| MSTest | xUnit v3 | Sites |
|---|---|---|
| `[TestClass]` | *deleted* — xUnit has no class marker | 53 |
| `[TestMethod]` with no `[DataRow]` | `[Fact]` | 344 |
| `[TestMethod]` with `[DataRow]` | `[Theory]` | 6 |
| `[DataRow(a, b)]` | `[InlineData(a, b)]` | 24 |
| `[DataRow(x, DisplayName = "…")]` | `[InlineData(x, TestDisplayName = "…")]` | 4 |
| `[TestInitialize] void F()` | the class constructor | 14 |
| `[TestCleanup] void F()` | `IDisposable.Dispose()` | 14 |
| `[Ignore("reason")]` on a method | `[Fact(Skip = "reason")]` | 3 |
| `[Ignore("reason")]` on a class | `Skip` on the class's single `[Fact]` | 1 |

`TestDisplayName` is the v3 replacement for MSTest's `DisplayName`; it is declared on
`Xunit.DataAttribute`, which `Xunit.InlineDataAttribute` derives from (confirmed present in
`xunit.v3.core.dll` 3.2.2 alongside `Skip` and `SkipUnless`). The four rows that use it are the
Gemini wire-format cases in `Transcription/GeminiWireTests.cs` — `"no candidate"`, `"no part"`,
`"no text"`, `"nothing at all"` — whose whole point is that the row is legible in the runner output,
so they must not silently become `InlineData` without it.

### D4 — Lifecycle: constructor and `Dispose`, no virtual chain

MSTest and xUnit agree on the thing that matters: both construct a fresh instance of the test class
per test method. So `[TestInitialize]` becomes the constructor body and `[TestCleanup]` becomes
`Dispose()`, with no change in when either runs.

The four abstract bases — `Logging/FileLoggingTestBase`, `Dictation/DictationTestBase`,
`Hotkeys/GlobalHotkeyServiceTestBase`, `Output/TextOutputTestBase` — each take a protected
constructor and implement `IDisposable`. Their eleven `sealed` derived classes need nothing at all:
**no derived class adds its own `[TestInitialize]` or `[TestCleanup]`**, so no `virtual Dispose` and
no `base` call is required anywhere. The one file that looks like a counter-example,
`Dictation/DictationLifecycleTests.cs`, holds two classes — `DictationLifecycleTests`, which derives
from the base and adds no lifecycle, and `DictationRegistrationTests`, which is standalone and owns
its own temporary-home pair.

`Dispose()` bodies that delete a temporary directory (nine of the fourteen) keep exactly today's
behaviour, including today's willingness to throw if the directory is gone.

### D5 — Class-level `[Ignore]` has no xUnit equivalent, and does not need one

`Audio/ManualCaptureSmokeTest` carries `[Ignore]` on the class. xUnit v3 cannot skip a class by
attribute, but the class holds exactly one test method, so
`[Fact(Skip = "Requires a real microphone; run manually")]` on
`RecordFiveSecondsAndWriteBothFormatsForPlayback` is an exact translation, not an approximation. The
XML doc comment explaining *why* it is manual stays on the class where it is.

### D6 — Accept xUnit's parallel-by-collection default

MSTest runs this suite strictly sequentially today (there is no `[assembly: Parallelize]` anywhere).
xUnit runs each test class as its own collection, in parallel, and this change keeps that default
rather than opting back out. The suite was audited for what that breaks, and the answer is nothing:

- **No static mutable state.** The only statics in `src/` are `KeyCodeMap.CanonicalKeyNames` (a
  readonly projection) and `SettingsJsonContext.OnDisk` (a `Lazy<T>` over an immutable context). The
  only statics in the tests are `static readonly` fixtures and `static` helper methods.
- **No process-global mutation.** Nothing calls `Environment.SetEnvironmentVariable` or touches
  `Environment.CurrentDirectory`. Every fixture that needs a home directory builds
  `Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"))` —
  collision-free by construction.
- **No shared hook.** The libuiohook single-static-callback hazard that `CLAUDE.md` warns about is
  not reachable here: every hotkey fixture constructs its own `SharpHook.Testing.TestProvider`
  (`TestThreadingMode.Simple`), and no test installs a real hook.
- **The timing boundaries are on a fake clock.** `DictationTestBase.Dictate` is
  `Hotkeys.Press(); Clock.Advance(duration); Hotkeys.Release();` over `FakeClock`, so the 50 ms
  minimum-duration and 200 ms debounce assertions are deterministic under any CPU load.
- **The polling budgets are generous.** `RecordingSink.WaitUntil` allows 10 s; the two explicit
  waits (`GlobalHotkeyServiceTests.cs:84`, `TextOutputRestoreTests.cs:142`) allow 5 s.

That leaves the wall-clock **upper** bounds. The first is `DictationLifecycleTests.cs:66`,
`elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2))` on a `StopAsync` that should return in
milliseconds. A 2 s ceiling on a near-instant operation survives contention; if it ever does not, it
is the single line to revisit, not the parallelism decision.

**This audit found only that one, and it was wrong.** `FileLoggingRotationTests`.`WritesDoNotStallTheCallingThreadWhenTheFileRolls`
carries a second and much tighter one — `p999.ShouldBeLessThan(500d)` over 10 000 writes — which the
sweep above missed because it is a latency percentile rather than a timeout, and nothing about it
reads as a timing boundary until it fails. It passes alone and fails every parallel run, at 2000–9000 µs
against its 500 µs bound. The resolution keeps the parallel default and the assertion: that one class
sits in a `[CollectionDefinition("wall-clock", DisableParallelization = true)]` collection, so it never
runs beside another. Relaxing the bound was rejected because the synchronous sink the asynchronous
wrapper replaced costs about 1700 µs, so any ceiling loose enough to survive contention would also
admit the thing the test exists to rule out. The generalisation for anyone auditing this suite again:
a latency **percentile** is a wall-clock upper bound too, and `ShouldBeLessThan` over a `Stopwatch` is
the shape to grep for, not `TimeSpan`.

**And the audit's whole frame was too narrow.** A third failure, found by running the `Category=Unit`
filter 25 times, was not a timing bound at all:
`HookProviderProbeTests.PostedEvent_KeepsItsMaskAndIsNotFlaggedAsSimulated` spins until
`hook.IsRunning` and then posts an event, and **`IsRunning` is not a readiness signal**. It turns true
before the provider's dispatch proc is installed; an event posted inside that window is answered with
`UioHookResult.Success` and then dropped permanently, so the failure appears six lines later as
`observed should not be null`. The signal that does mean ready is the hook's `HookEnabled` event,
which is itself delivered through the dispatch proc and therefore cannot arrive before one is
installed. Its sibling `SuppressedEvent_IsRecordedByTheProvider` has the identical shape and did not
fail in 25 runs. Both now wait for `HookEnabled` before posting, and for the handler before `Stop`,
which does not wait for an in-flight dispatch.

The first attempt at this fix was wrong and is worth recording: waiting on the *handler* while still
spinning on `IsRunning` turned a null-reference-shaped failure into a five-second timeout at the same
1-in-25 rate, because the event was already gone by then. What settled it was an in-process A/B — 3000
iterations of each readiness signal under a deliberately starved thread pool — which measured 2 drops
for `IsRunning` and 0 for `HookEnabled`. On an idle machine both read 0 in 3000, which is exactly why
the sequential suite never saw it.

So D6 swept for static state, process-global mutation, shared hooks and wall-clock bounds, and a test
that simply synchronises on the wrong event passes every one of those checks. The frame to use next
time is not "what state is shared" but "what does this test assume has already happened".

### D7 — Fix what `xunit.analyzers` flags; suppress nothing

`xunit.analyzers` 1.27.0 arrives with the framework and its diagnostics are warnings, which
`TreatWarningsAsErrors` turns into build failures. Both rules this section turns on are present in
that version, checked rather than assumed: `DoNotUseBlockingTaskOperations` (xUnit1031) and
`UseCancellationToken` (xUnit1051). Three sites block on a task (xUnit1031):

- `Logging/FileLoggingBufferTests.cs:79` and `Logging/FileLoggingRegistrationTests.cs:97` —
  `host.StopAsync(…).GetAwaiter().GetResult()`, both inside synchronous test methods that become
  `async Task` and `await`.
- `Hotkeys/BlockingHookProvider.cs:35` — `_released.Wait()`. This one is a **deliberate** block: the
  type exists to simulate a hook provider that never returns, which is what change 6's five-second
  startup timeout is tested against. It is a test double, not a test method, so the analyzer should
  not be reaching it; if it does, the fix is a scoped `#pragma warning disable` with the reason
  written next to it, not a project-wide `NoWarn`.

xUnit1051 (pass a `CancellationToken`) will fire across the many `Task.Delay` calls. The fix is
`Xunit.TestContext.Current.CancellationToken`, which is v3's ambient per-test token and also buys
the suite proper cancellation on runner shutdown. The exact diagnostic list is whatever the compiler
reports; the rule is that each one is fixed or scope-suppressed with a written reason, and
`Directory.Build.props` gains no `NoWarn`.

### D8 — `global using Xunit;` per project, not `using Xunit;` in 61 files

MSTest supplied its namespace through an implicit global using, which is why no test file names it.
Adding a two-line `GlobalUsings.cs` to each test project preserves that property exactly and keeps
the 61-file diff to attribute renames. xUnit v3 does **not** add implicit usings of its own
(confirmed: `xunit.v3.core.mtp-v1.props` and `.targets` define no `Using` items), so this file is required, not a
convenience. It is also consistent with the repository, which already runs on
`ImplicitUsings=enable`.

### Windows and macOS

The migration itself is platform-neutral: both test projects are plain `net10.0` with no RID, and
`dotnet build src/Pisum.Whisper.App -r osx-arm64` does not build them. Two platform-specific points
do fall out of it:

- The four manual tests are the suite's only platform-facing coverage, and two of them
  (`Output/ManualClipboardRoundTrip`, `Dictation/ManualDictationSmokeTest`) must be run by hand on
  **both** Windows and macOS. D2 changes the command used to run one test by name on both platforms
  equally, so the replacement incantation has to be verified on Windows and written down before this
  change is done; a macOS developer must not be left deriving it.
- D6 raises a new hazard for those same four tests, on both platforms: un-skipping one now runs it
  concurrently with the rest of the suite, contending for the one real clipboard or the one real
  microphone. They are meant to be run alone by filter, which is what the archived tasks already
  say; this change does not alter that and does not need to guard it in code.

### Rejected alternatives

- **`xunit.v3` 4.0.0** — current stable, and it would match how every other package here is pinned.
  Rejected because it is not the version `Avalonia.Headless.XUnit` 12.1.1 is compiled against, and
  the mismatch resolves silently rather than failing at restore (D1).
- **The VSTest bridge (`xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk`)** — keeps `--filter`
  working and is indifferent to the MTP version, at the cost of three package references where one
  does and of staying on the platform xUnit v3 is moving off. Still the standing fallback for both
  the Rider risk and the MTP 1.9.1 risk below.
- **`[assembly: CollectionBehavior(DisableTestParallelization = true)]`** — would reproduce MSTest's
  sequential execution, but D6 found nothing for it to protect.
- **`<NoWarn>$(NoWarn);xUnit1031;xUnit1051</NoWarn>`** — cheaper than D7, and it would put a standing
  suppression into the repository whose only quality gate is warnings-as-errors.
- **`IAsyncLifetime` / `IClassFixture`** — solves problems this suite does not have; all fourteen
  lifecycle methods are synchronous and nothing is shared across tests.
- **`using Xunit;` added to all 61 files** — a larger diff that would also make the test files name
  their framework for the first time.

## Risks / Trade-offs

- **Rider cannot discover MTP tests** → The IDE test runner is how this project is developed
  (`CLAUDE.md`: "developed in JetBrains Rider on Windows"). This is verified as an explicit task
  before the migration is called done, not assumed. If discovery fails, the fallback is the rejected
  VSTest bridge — add `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` back and drop the
  `global.json` `test` block; nothing in D3–D8 depends on the runner choice.
- **A `[Theory]` silently loses a row** → `[InlineData]` and `[DataRow]` have the same
  constant-expression restriction and there are only 28 rows, but a miscount is invisible in a green
  run. Mitigated by asserting the total: 372 tests discovered, 368 passing, 4 skipped, exactly as
  today.
- **A `Dispose()` that no longer runs** → Losing a `[TestCleanup]` translation leaks a temp directory
  and, worse, leaks an undisposed `GlobalHotkeyService` or `SerilogLoggerFactory` into a now-parallel
  run. Mitigated by counting: 14 `[TestCleanup]` in, 14 `IDisposable` implementations out.
- **The parallelism audit missed something** → The symptom is a test that passes alone and fails in
  the suite, and it will not necessarily fail on the first run. Mitigated by running the migrated
  suite repeatedly rather than once, and by D6 having named the one line that would need revisiting.
- **The pin moves Microsoft.Testing.Platform backwards, 2.3.3 to 1.9.1** → MSTest 4.3.3 resolves MTP
  2.3.3 today; `xunit.v3` 3.2.2 resolves 1.9.1. Whether the .NET 10 SDK's MTP-mode `dotnet test`
  drives a 1.9.x app is unverified, and it gates everything after it. Task 2.3 establishes this
  before any test file is rewritten; the fallbacks are the VSTest bridge or 4.0.0 with the Avalonia
  question deferred (D2).
- **The Avalonia incompatibility is predicted, not observed** → Nobody has run `[AvaloniaFact]`
  against `xunit.v3` 4.0.0; D1 reasons from an assembly-version delta plus extensibility coupling.
  The pin is the cheap insurance either way, but the claim should be settled rather than inherited:
  task 5.5 resolves `Avalonia.Headless.XUnit` against the pinned version and runs one throwaway
  headless test, which is what change 10 would otherwise discover the hard way.
- **Avalonia moves to xunit.v3 4.x before change 10** → Then the pin is the stale one. Cheap to
  revisit: re-read `Avalonia.Headless.XUnit`'s dependency group when change 10 opens, and bump both
  together if it has moved.

## Migration Plan

The change is a single commit's worth of work with a trivial rollback — it touches no file under
`src/`, so `git revert` restores a fully working state and cannot leave the application in a
half-migrated one. Ordering within it matters only in that the build must be broken as briefly as
possible:

1. Packages and platform first (`Directory.Packages.props`, `global.json`, both `.csproj`, the two
   `GlobalUsings.cs`). The solution does not compile after this step — that is expected.
2. `Pisum.Whisper.Platform.Tests` next: 3 files, 3 `[TestClass]`, 4 tests. It is the smallest
   possible end-to-end proof that the packages, the runner and the mapping in D3 all work.
3. `Pisum.Whisper.Core.Tests` by directory, in the order `Settings`, `Logging`, `Audio`, `Hotkeys`,
   `Transcription`, `Output`, `Dictation` — cheapest and most independent first, and the four base
   classes reached late, once the pattern is settled.
4. Analyser diagnostics (D7) once the whole thing compiles, so the full list is visible at once
   rather than one file at a time.
5. Documentation (`README.md`, `openspec/config.yaml`, `CLAUDE.md`) last, when the commands in it
   have actually been run.

## Verification results

Run on 2026-08-31 on win-x64 (Windows 11 Pro 10.0.26200), SDK 10.0.400, against the real solution
plus two throwaway projects in the scratchpad. **No macOS run has happened**, so the two manual tests
that must be exercised on both platforms (`ManualClipboardRoundTrip`, `ManualDictationSmokeTest`)
are still open on osx-arm64.

| Task | What was checked | Result |
|---|---|---|
| 2.2 | The transitive graph D1 predicts from one `xunit.v3` reference | **PASS** — `xunit.v3.core.mtp-v1` 3.2.2, `xunit.v3.extensibility.core` 3.2.2, `xunit.v3.assert` 3.2.2, `xunit.analyzers` 1.27.0, `Microsoft.Testing.Platform` **1.9.1**, exactly as drawn |
| 2.3 | Whether the .NET 10 SDK's MTP command drives a 1.9.x app — *the gate this change rested on* | **PASS** — `dotnet test --help` reports the MTP command, and a throwaway single-`[Fact]` project on `xunit.v3` 3.2.2 ran and passed. No fallback needed |
| 4.1 | The full `xunit.analyzers` diagnostic list, collected in one pass | 2 × xUnit1031, 25 × xUnit1051, and **nothing at `BlockingHookProvider.cs:35`** — the analyser does not reach the test double, so D7's contingent `#pragma` was never needed |
| 4.4 | `dotnet build Pisum.Whisper.slnx` | **PASS** — 0 warnings, 0 errors, no `NoWarn`, no `#pragma`, no suppression of any kind |
| 5.1 | The suite is whole | **PASS** — 372 total, 368 passed, 4 skipped; Core 368, Platform 4, split exactly as the 1.1 baseline. ~5.6 s against 12 s sequential |
| 5.2 | Five consecutive suite runs (D6) | **PASS after a fix** — see below; the audit had missed a bound |
| 5.3 | Running one manual test by name | **PASS** — the real clipboard round trip ran and passed. See the command below |
| 5.5 | `Avalonia.Headless.XUnit` 12.1.1 against the pinned `xunit.v3` 3.2.2 — *the claim the pin rests on* | **PASS** — restores with no downgrade warning, resolves `xunit.v3.extensibility.core` 3.2.2, and one `[AvaloniaFact]` constructing a `Button` in a `Window` runs green |

**The pin is now observed rather than reasoned.** `Avalonia.Headless.XUnit` 12.1.1's `net10.0`
dependency group names `xunit.v3.extensibility.core` **3.2.2** in its nuspec, and a throwaway project
referencing both resolves cleanly and runs an `[AvaloniaFact]`. What is still *not* observed is the
negative case: nobody has run `[AvaloniaFact]` against `xunit.v3` **4.0.0** to confirm it actually
breaks. D1's argument for 4.0.0 being dangerous — a moved assembly version behind a silent resolve —
is unchanged and untested, and the pin costs nothing either way.

**D6's audit was incomplete, and the suite is not perfectly stable.**
`FileLoggingRotationTests.WritesDoNotStallTheCallingThreadWhenTheFileRolls` asserts a p99.9 write
latency under 500 µs; run beside the other 52 classes it measured 2000–9000 µs and failed every run.
Isolating that class in a `DisableParallelization` collection made task 5.2's five consecutive runs
pass. It did **not** make the test deterministic: across roughly 22 further runs it still failed about
three times, once at 664 µs in steady state and twice on runs that also rebuilt. The bound is
inherently machine-sensitive and this is a known flake going into change 12's CI, not a settled
matter.

**Running one manual test by name** — this replaces the VSTest `--filter` syntax the archived tasks
quote, and is verified on Windows:

```bash
dotnet test tests/Pisum.Whisper.Platform.Tests \
  --filter-method '*ManualClipboardRoundTrip.ATokenSurvivesAWriteAndAReadBack' \
  -e PISUM_WHISPER_RUN_MANUAL=1
```

The environment variable is required because xUnit v3 has **no runner option that can run a skipped
test** — `-explicit` covers explicit tests only, and the full option list has nothing else. A plain
`[Fact(Skip = …)]`, which is what D3's mapping produced, leaves the four manual tests unrunnable
rather than merely unrun. They are therefore gated on `ManualTests.Enabled` through `SkipUnless`, so
the default run still reports them skipped **with their reason**, and setting the variable runs them.
That is a departure from D3's pure attribute rename, taken deliberately so task 5.3 could be
satisfied at all.

**One correction to this document.** D3 and task 3.5 place the four `DisplayName` rows in
`Transcription/GeminiWireTests.cs`. They are in `Transcription/GeminiProviderTests.cs`;
`GeminiWireTests.cs` holds no data rows at all. The rows were migrated correctly regardless, and
`no candidate`, `no part`, `no text` and `nothing at all` are still their displayed names.
