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

### D1 — One package: `xunit.v3` 4.0.0

`Directory.Packages.props` gains `<PackageVersion Include="xunit.v3" Version="4.0.0" />` and loses
`MSTest` and `Microsoft.NET.Test.Sdk`. `SharpHook.Testing`, `FakeItEasy` and `Shouldly` are untouched.

The single reference is sufficient because of what it pulls (verified against the published nuspecs):

```
xunit.v3 4.0.0
└── xunit.v3.mtp-v2 4.0.0
    ├── xunit.v3.core.mtp-v2 4.0.0     the framework + the in-process MTP runner
    ├── xunit.v3.assert 4.0.0          unused here; Shouldly stays
    └── xunit.analyzers 2.0.0          see D7
```

`buildTransitive/xunit.v3.core.mtp-v2.targets` raises a hard MSBuild error unless `OutputType` is
`Exe` — already true of both projects, so no property changes.

Version 4.0.0 is the current stable of the xUnit.net **v3** line (the package version numbering runs
independently of the "v3" in the package id) and was published 2026-08-15. Taking it rather than
3.2.2 matches how the rest of this repository is pinned: .NET 10, Avalonia 12.1.1, MSTest 4.3.3 and
`Microsoft.NET.Test.Sdk` 18.9.0 are all current-stable.

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
`xunit.v3.core.dll` 4.0.0 alongside `Skip` and `SkipUnless`). The four rows that use it are the
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

That leaves exactly one wall-clock **upper** bound in the suite —
`DictationLifecycleTests.cs:66`, `elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2))` on a
`StopAsync` that should return in milliseconds. A 2 s ceiling on a near-instant operation survives
contention; if it ever does not, it is the single line to revisit, not the parallelism decision.

### D7 — Fix what `xunit.analyzers` flags; suppress nothing

`xunit.analyzers` 2.0.0 arrives with the framework and its diagnostics are warnings, which
`TreatWarningsAsErrors` turns into build failures. Three sites block on a task (xUnit1031):

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
(confirmed: `xunit.v3.core.mtp-v2.props` and `.targets` define none), so this file is required, not a
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

- **`xunit.v3` 3.2.2** — more soak time, but every other package here is pinned to current stable.
- **The VSTest bridge (`xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk`)** — keeps `--filter`
  working, at the cost of three package references where one does and of staying on the platform
  xUnit v3 is moving off.
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
- **`xunit.v3` 4.0.0 is two weeks old** → A regression in a fresh major lands on this project first.
  Accepted: the fallback is pinning 3.2.2 in `Directory.Packages.props`, a one-line change, since
  nothing in D3–D8 uses 4.0-only API except `TestDisplayName`, which 3.x also has.

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
