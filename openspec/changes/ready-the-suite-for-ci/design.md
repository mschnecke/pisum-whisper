## Context

See `proposal.md` — *Why*. What follows is measured on Apple Silicon (macOS 26.6.2, SDK `10.0.400`)
on 2026-09-03, against `main` at `d8a596d`.

The full run, and what is in it:

```
 629 tests
  |
  +--   4  Manual (mic / clipboard / keyboard / API key)  --> filtered by --filter-not-trait
  |
  +--   5  WindowsAutostartTests                          --> SKIPPED, with a reason
  |          [Fact(Skip = "The Windows registry is not reachable on this operating system",
  |                 SkipUnless = nameof(WindowsOnly.Enabled), SkipType = typeof(WindowsOnly))]
  |
  +--   5  PresetsViewModelTests (4)                      --> FAIL
  |        SettingsEditorTests (1)
  |
  +--   1  FileLoggingRotationTests p99.9                 --> intermittent FAIL
  |
  +-- 614  everything else                                --> pass
```

**The two failing groups are the same category of problem as the skipping one** — a test that only
makes sense on Windows — handled to two different standards. That symmetry is the whole design.

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
  platforms.
- Every test that does not run somewhere says so **in the runner output, with a reason**, rather
  than being filtered away by a flag in a workflow file.
- `add-packaging-ci`'s CI invocation needs exactly one filter.

**Non-Goals:**

- Deciding what the rotation latency bound should be. That is `file-logging`'s.
- Auditing the other 614 tests for latent platform assumptions beyond the full run above.

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

## Migration Plan

1. Re-run the probe on Windows (task 1.1). If T1 does not throw there, fall back to T3, which needs
   the same one-line arrangement and no base-class change.
2. The five tests and the timing gate land together, in one pull request, verified by a full run on
   both platforms by hand.
3. `add-packaging-ci`'s D9, its spec requirement and its task 5.2 are amended in the same pull
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
