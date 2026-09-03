## Context

Issue #30 lists eleven checks across seven archived changes that need only this machine — Windows 11
Pro 10.0.26200, the one the 2026-09-02 run of `report-startup-failures` used — and none is blocked on
anything:

| Change | Task | What it settles |
|---|---|---|
| 8 `add-dictation-pipeline` | 6.1 | the first end-to-end dictation, hold and toggle |
| 8 | 6.3 | whether 120 s is the right transcription budget |
| 9 `add-tray-icon` | 4.1 | the tray icon, its overflow placement and its tooltip |
| 10 `add-settings-window` | 6.3 | whether 400 ms is the right commit delay |
| 10 | 6.4 | the recorder does not fight the running hotkey |
| 11 `add-system-integration` | 7.1 | first launch, forced failures, focus, the `Run` key |
| 11 | 7.4 | the pooled-thread dispatcher question, and alt-tab |
| 7 `add-text-output` | 3.3 | clipboard history exclusion |
| `migrate-tests-to-xunit-v3` | 5.4 | Rider discovers the tests |
| `report-startup-failures` | 7.1 | three of the four fatal reproductions |
| `report-startup-failures` | 7.3 | `MessageBoxW` at login |

What the checks touch, as the tree stands at `ec8fe69`:

- `DictationOrchestrator.DefaultTranscriptionBudget` (`Core/Dictation/DictationOrchestrator.cs:69`),
  120 s, injected through the internal constructor so no test asserts the number. The watchdog stops
  a recording at `AppSettings.MaxRecordingDurationSecs`, default 600, and the pipeline transcribes
  what it has; the orchestrator logs `"Transcribing a {Seconds:F1} s recording of {SampleCount}
  samples as {Format}."` at Information before the upload and the delivery or failure line after it.
- `SettingsEditor.CommitDelay` (`App/Settings/SettingsEditor.cs:32`), 400 ms, injected the same way.
  `Commit` logs `"Committing {ChangedSettings} settings changes."` at Debug. The `settings-window`
  spec names no number — "persisted once the typing has stopped" — so the constant can move without
  a spec change.
- `ToastPresenter.Present` (`App/Notifications/ToastPresenter.cs:61`) is one line,
  `Dispatcher.UIThread.Post(() => Show(title, message))`. `Program.Main` runs `host.Start()` at line
  64 and `StartWithClassicDesktopLifetime` at line 69; the orchestrator's hotkey handlers are armed by
  the first and Avalonia's dispatcher is created on the main thread by the second. `App` already
  resolves the concrete `ToastPresenter` for `CloseAll` (`App.cs:431`), and `Program.cs:170`
  registers it as a singleton with `INotificationPresenter` forwarding to it.
- `App.LoadIcon` (`App.cs:258`) opens `avares://Pisum.Whisper.App/Assets/{name}.png`. `Assets\**`
  are `AvaloniaResource` items compiled into `Pisum.Whisper.App.dll`; the build output holds no
  `.png` at all.
- `SettingsStore.Load` (`Core/Settings/SettingsStore.cs:54`) takes the first-launch branch when
  `File.Exists` is false and calls `Write`, which writes `~/.pisum-whisper.json.tmp` and
  `File.Move`s it over the target. `StartupFailure.Describe` maps `UnauthorizedAccessException or
  IOException` to the settings-error title with a message naming the path.
- `ToastPresenterTests.PresentCompletesWhenCalledFromANonUiThread` already posts from `Task.Run` —
  after `[assembly: AvaloniaTestApplication]` has initialised Avalonia for the whole assembly.
- The previous run's record is the format: a *Verification results* section in the archived
  `design.md` with a header naming date, machine and build, a `| # | What was checked | Result |`
  table, and a paragraph on what was not run; open questions struck through with the answer in
  bold, in change 9's manner.

**Why the debt exists, and what it is made of.** Twelve changes' worth of code landed between
2026-08-27 and 2026-09-02 — changes 7, 8 and the test migration on 08-31, 9 and 10 on 09-01 — each
ending its `tasks.md` with a group of by-hand checks in the same checkbox syntax as the code tasks.
The archive skill counts open boxes, warns and asks to confirm; every archive from 7 onward went
through that confirmation, correctly, because the roadmap is strictly sequential from 8 to 11 and
holding 9 until 8 had been run by hand would have stalled the tail. The debt is the throughput gap
between implementing a change in hours and verifying it in an afternoon, made invisible by two
habits: filing every open box under "one Apple Silicon sitting", and writing verification harnesses
in the scratchpad and throwing them away — change 7's clipboard run and the 2026-09-02 fatal-dialog
run were both scripted, and neither script survives, in a repository whose `spikes/` exists so that
a harness can be re-run rather than re-written. What the tasks never carried is which of three
things each check needs:

| Needs a person: judgment, speech, an IDE, a login | Needs a desktop session, not a person: a window, a clipboard, a hook, a dialog | Needs hardware: a microphone, Apple Silicon, a 44.1 kHz device |
|---|---|---|
| 8/6.1 speak and watch the paste | 10/6.4 the recorder against the running hotkey | 8/6.3 ten minutes of speech |
| 9/4.1 look at the tray icon | `report-startup-failures` 7.1's three reproductions: launch, wait for the dialog, post `WM_CLOSE`, assert the exit code and the `Fatal` line | 11/7.1's no-microphone half |
| 10/6.3 judge the commit delay | 11/7.4 the gate's tests, once landed | everything on #31 |
| `migrate-tests` 5.4 the IDE window | 7/3.3 clipboard history — WinRT can read it, but not from a plain `net10.0` target, so it stays a glance | 1/1.5a |
| `report-startup-failures` 7.3 sign out and in | | |
| 11/7.1 the welcome and the switch | | |

Roughly a third of the Windows debt is manual only because nobody kept the script. GitHub's Windows
runners run in an interactive session, so change 12's CI can run the middle column; it cannot
supply a microphone or a person. Decision 1 records the rule that decides where a run is recorded;
task 7.1 adds two more to the roadmap: open by-hand tasks move to a tracking issue at archive, and a
harness written for a desktop-session check is kept under `spikes/`. Tasks 5.1 to 5.3 are the first
application of the second, as `spikes -- fatal`.

## Goals / Non-Goals

Goals:

- Every one of the eleven checks has run, and its result is recorded where its question was asked.
- Both constants are confirmed or moved once, with the measurement written down.
- 7.4 is answered by running, and the transport is gated if the answer demands it.
- The roadmap, `README.md` and `CLAUDE.md` say what is now true, and issue #30 is closed.

Non-goals:

- Anything needing Apple Silicon (#31), and change 1's 1.5a.
- Fixing a toast found in the alt-tab list, or the rebind log line on every save.
- Turning a manual check into an automated one: the two spec scenarios added here are verified by
  hand, and the headless tests are for the contingent gate only.

## Decisions

### 1. Results are recorded where the questions live, and this change keeps an index

Each check writes into the archived change that asked it: a row in that `design.md`'s *Verification
results* — created where absent, which is changes 8, 9, 10 and 11; extended in 7 and
`report-startup-failures` — the open question it settles struck through with the answer in bold, and
the box in that `tasks.md` ticked. A box is ticked only when the whole task ran:
`report-startup-failures` 7.1 has four reproductions and 11's 7.4 two halves, and a partial run
stays unticked with a note, which is what the 2026-09-02 commit did. This change's own `design.md`
gains a *Verification results* table with one row per check naming the archived section that holds
the record, so the change is self-describing after archive without duplicating a result.

A ticked box certifies that a check ran, not that it passed. A `FAIL` is recorded as one, with what
was observed, and the box is ticked all the same — the roadmap's rule.

**No transcript text goes into any record.** The rule that keeps it out of the log and the
notification applies to `design.md` with more force, since the repository is public. What is
recorded is what the log carries: character counts, durations, outcomes.

**Why a change and not a "Record …" commit — and the rule for next time.** The commit that closed
#20 (`ec8fe69`) recorded a partial run straight into the archived design and the roadmap, with no
change folder, and issue #30 already carries the eleven boxes. That pattern is the right one for a
run that only records. This is a change because its runs do more than record: a requirement on
`notifications` the code does not yet meet, a scenario on `dictation-pipeline` whose number may
move, a corrected archived task, and design made now — the `FromThread` gate, the timeout rules —
which does not belong in an archived change's `design.md` beside decisions made weeks earlier.
Striking a question through with its answer is the archive's convention; adding new design there is
not. The rule, which task 7.1 puts under the roadmap's *Standing decisions*: a verification run that
only records goes in a "Record …" commit against the tracking issue; one that changes code, moves a
constant a spec names, or discovers a requirement goes through a change.

### 2. The order is 8/6.1 first, and two builds serve the lot

6.1 runs first because 10/6.3 is judged during it and because every later check is easier once a
dictation is known to work. The login-time dialog (`report-startup-failures` 7.3) runs last, because
signing out ends the session everything else is being written up in.

Two builds, chosen per check by what the check is about:

| Build | Launched by | Used for |
|---|---|---|
| Debug, `dotnet run --project src/Pisum.Whisper.App` | the terminal | 8/6.1, 8/6.3, 10/6.3, 10/6.4, 7/3.3, 11/7.1, 11/7.4 — the console echo of the log is the feedback loop |
| Release, `dotnet build src/Pisum.Whisper.App -c Release` | `Start-Process` on `src/Pisum.Whisper.App/bin/Release/net10.0/Pisum.Whisper.App.exe` | 9/4.1, the three fatal reproductions, 7.3 — having no console is part of what is being checked |

`Start-Process` from a session stands in for Explorer; the 2026-09-02 run established that a `WinExe`
started that way has no console. Autostart is reconciled on every launch, so while the checks run the
`Run` key follows the settings file's `startWithSystem` and names whichever build was launched last.
That is the `autostart` capability working, not a side effect to suppress; the value is read before
the first check and put back after the last.

### 3. User state is backed up by hash before the first check that touches it, and restored by each

Four checks delete or corrupt `~/.pisum-whisper.json`, which holds API keys in plaintext; two change
the `Run` key; one changes clipboard history. Before any of them, the file is copied to the
scratchpad and its SHA-256 recorded, and the `Run` value `Pisum Whisper` under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and
`HKCU\Software\Microsoft\Clipboard\EnableClipboardHistory` are read and noted. Every task that alters
one ends by restoring it and, for the file, re-hashing — the 2026-09-02 run's discipline, applied to
every task rather than one. A task that ends with the hash differing is not done.

### 4. The budget is measured from the log against a watchdog-stopped recording, and the request timeout with it

**Neither number has a reference to defer to.** `gemini.rs` sets `MAX_RETRIES = 3` and
`RETRY_DELAY_MS = 1000` and no timeout at all — reqwest's default is none — so a hung upload hangs
the reference's dictation for ever. The 60 s per request (`GeminiHttpClient.Timeout`, change 5) and
the 120 s budget (`DefaultTranscriptionBudget`, change 8) are this codebase's, chosen from the shape
of the retry policy. Neither the `gemini-transcription` nor the `dictation-pipeline` spec names the
60 s, so it moves without a spec edit; the 120 s is named, and rides in this change's delta.

**The request timeout is the constant that bites first, and the arithmetic says where.** The
encoder pins Opus at 24 kbps (`OggOpusWriter.Bitrate`) and base64 inflates it by a third, so every
second of speech is 32 kbps on the wire: a 600 s recording is about 2.4 MB of request body. Per
attempt, time is `T = a + s × (u + m)` — a fixed overhead, plus the recording length times an upload
cost per second (32 kbps over the uplink) plus a model cost per second. Under a 60 s timeout the
upload alone needs a sustained uplink above roughly 320 kbps before the model has spent a second,
nearer 430 kbps with 15 s of model time. A hang is detected by that timeout at 60 s, never by the
budget at 120 s; the budget's job is to cap attempts times keys, and it bites only after the timeout
already has. So a short dictation on a hung link waits out the budget for speech that costs five
seconds to repeat, while a long dictation on a slow uplink is cut by the timeout on every attempt
and loses ten minutes. The second is the case that matters, and it is the timeout's.

**Four recordings, not three, so the slope is measured.** A 60 s control and a 300 s middle in
toggle mode, and two at the 600 s maximum stopped by the watchdog at the default so the length is
exact rather than eyeballed — all in Opus. Speech is played at the microphone for the duration — a
podcast will do — because silence transcribes in no time and measures nothing. The round trip is the
interval between the `Transcribing a … s recording` line and the delivery or failure line after it;
the log's one-second resolution is enough for numbers this size, and it leaves a record a stopwatch
would not. Two points give `u + m` on the development connection; three confirm it is a line. Opus
and not WAV: at 48 kHz mono, WAV passes the 14 MiB inline ceiling after about 2 min 33 s
(`IAudioCapture.SampleRate`'s remarks), so a 600 s WAV recording fails before it uploads — a known
limit, not this measurement.

The record carries the date, the model id from settings, the connection and its measured uplink,
the sample counts, the four timings and the slope. Two decision rules, fixed before the measurement
so the numbers are not argued after it:

- **The request timeout:** 60 s stands if the slowest 600 s attempt is under 30 s, half of it;
  otherwise `GeminiHttpClient.Timeout` becomes twice the slowest attempt, rounded up to a round
  number. A 600 s attempt over 60 s is cut by the timeout itself, which is the strongest evidence
  there is and is recorded as a `FAIL` of the current number.
- **The budget:** it must hold one full attempt, the first wait and a second full attempt — twice
  the request timeout plus the waits — so 120 s stands while the timeout stays at 60 s and follows
  it if the timeout moves.

If either moves, it moves once: the constant, its doc comment, `CLAUDE.md`'s "60 s timeout" and
"120 s budget" sentences, and for the budget the number in this change's
`specs/dictation-pipeline/spec.md` so the archive sync carries it.

**A length-dependent rule is deliberately not chosen here.** Scaling the budget down for short
recordings would bound the wait; scaling the request timeout up with the byte count — provider
knowledge, a per-call `CancelAfter` in `GeminiProvider` — would save the long dictation on the slow
uplink. Both are real, and both chosen today would be derived, which is the mistake 3.1 exists to
correct. The measured slope and the uplink arithmetic are recorded beside the WAV ceiling as a known
limit, so a follow-up proposal starts from numbers.

### 5. 7.4 is a forced-ordering experiment in `Program.Main`, not a headless test

The archived task asks for "a headless test posting from a pooled thread plus the deliberate early
call". The first half cannot say what it was meant to: `Pisum.Whisper.App.Tests` declares
`[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`, which initialises Avalonia once for
the assembly, so every test runs after a `Dispatcher.UIThread` exists on the headless loop, and
`PresentCompletesWhenCalledFromANonUiThread` is already the post-initialisation pooled-thread case. A
test cannot un-initialise, and an xUnit test thread is itself a pool thread, so even a fresh assembly
could not stand in for `[STAThread] Main`. The case exists only where initialisation has not
happened, which is `Program.Main` between `host.Start()` and `BuildAvaloniaApp`.

The experiment is a throwaway edit there, never committed:

```csharp
// After host.Start(), before BuildAvaloniaApp.
Task.Run(() => host.Services.GetRequiredService<INotificationService>()
        .Notify("Early", "Raised from a pooled thread before Avalonia was initialised."))
    .GetAwaiter().GetResult();
```

Blocking the main thread until the pooled one has returned is what makes the ordering deterministic:
the pooled thread has gone through `Present` and touched `Dispatcher.UIThread` before the main thread
reaches `StartWithClassicDesktopLifetime`. A real capture failure from a hotkey press in that window
is the same path and cannot be timed by hand. `Notify` rather than `Present` directly, so the policy
is in the path too; it is forced, so the preference is irrelevant.

**Read at the tag, the experiment is predicted to fail, and to fail fast.** At 12.1.1
(`src/Avalonia.Base/Threading/Dispatcher.cs` and `Dispatcher.ThreadStorage.cs`) the constructor
stores `_thread = Thread.CurrentThread` and does `s_uiThread ??= this`; `CheckAccess` is
`Thread.CurrentThread == _thread`; and the `UIThread` getter is `s_uiThread ?? CurrentDispatcher`,
where `CurrentDispatcher` creates a dispatcher for whatever thread is asking. So the first `Present`
from the pooled thread creates the process's UI dispatcher on that thread. Then
`Win32Platform.Initialize` calls `Dispatcher.InitializeUIThreadDispatcher`, whose first line is
`UIThread.VerifyAccess()` — on the main thread, against a dispatcher owned by the pooled one — and
throws `InvalidOperationException` "The calling thread cannot access this object because a different
thread owns it." That leaves `StartWithClassicDesktopLifetime` into `Main`'s catch:
`StartupFailure.Describe`'s `_ =>` arm, a `Fatal` line, the "could not start" dialog, exit 1. Nothing
in `src/` touches the dispatcher on the main thread before setup — the only touches are `Present`
and three posts in `App` subscribed after initialisation — so nothing pre-empts it. Pull request
18686 made `Dispatcher.UIThread` legal from `Main` before initialisation; from any other thread it
is fatal by construction.

In the product that is a hotkey press between `host.Start()` returning and
`Win32Platform.Initialize` running, whose capture failure or "Transcription In Progress" reaches
`Present` on the dispatch loop. The window is Avalonia's setup time: narrow, but open at every launch
including autostart at login, with the chord armed throughout it. What lands is not a lost toast but
a startup failure whose dialog says "could not start" and whose log names a thread-ownership
exception nobody would connect to a keypress.

The experiment still runs, because a prediction is not a run, and it records four things: the
`Fatal` line and its exception; whether the dialog appeared; whether, against prediction, the tray
icon came up and swapped on a dictation (the `ApplyState` post at `App.cs:103`); and whether a later
notification rendered. Matching the prediction is a pass of the reading and a fail of the current
code; any other outcome is the more interesting finding and is recorded as such.

**The gate is therefore expected, not contingent, and it asks the dispatcher rather than `App`.**
`Dispatcher.FromThread(Thread)` is public. `ToastPresenter` is constructed on the main thread, inside
`host.Start()`, so it captures `Thread.CurrentThread` then and, in `Present`, asks whether that
thread has a dispatcher yet:

```csharp
var dispatcher = Dispatcher.FromThread(_uiThread);
if (dispatcher is null)
{
    _logger.LogWarning("A notification was raised before the UI was ready and was dropped: {Title}", title);
    return;
}

dispatcher.Post(() => Show(title, message));
```

If the main thread owns a dispatcher, it is or will become the UI one — the first dispatcher wins,
and this one was created on the main thread — so posting to it is safe, at worst late. If it owns
none, touching `UIThread` would create the wrong one, so the notification is dropped and logged with
its title only, per the `notifications` delta. No `App` edit, no ordering dependence on
`OnFrameworkInitializationCompleted`, and the thread is injected through the internal constructor so
a test can name it. That needs `ILogger<ToastPresenter>` through the public constructor; the existing
four tests pass `NullLogger<ToastPresenter>.Instance`. Two headless tests join them:
`PresentBeforeTheUiThreadHasADispatcherIsDroppedAndLogged`, which constructs the presenter naming a
fresh thread that owns no dispatcher and asserts the drop and the warning — the negative case the
archived task wanted, which `MarkReady()` could not express and this shape can — and
`PresentAfterTheUiThreadHasADispatcherShows`, the existing behaviour restated against the gate. Then
the experiment runs again: the early notification is dropped with a warning, the application comes
up, and a later one renders.

The alt-tab half is a glance during 11/7.1: hold Alt+Tab with a toast up. If it is listed, that is
recorded as the blemish the archived design calls it, and the fix — `WS_EX_TOOLWINDOW`, which
Avalonia does not expose — is a proposal of its own.

### 6. Two fatal reproductions are corrected before they are run, and the third is unchanged

**The missing tray asset cannot be renamed in the build output**, because there is nothing there to
rename: `Assets\**` are `AvaloniaResource` items embedded in `Pisum.Whisper.App.dll`. The
reproduction moves `src/Pisum.Whisper.App/Assets/tray-idle.png` — the first icon the `App`
constructor loads — out of the tree to the scratchpad, builds Release, and launches; `AssetLoader.Open`
throws inside the constructor, inside `StartWithClassicDesktopLifetime`, and reaches `Main`'s catch
as the `_ =>` arm, the "could not start" title. The file is moved back and Release rebuilt. The
archived task is annotated with the correction rather than silently satisfied by something else.

**The unwritable settings file with none present** is a directory of the same name. `Load` tests
`File.Exists`, which is false for a directory, takes the first-launch branch and calls `Write`;
`File.Move` of the `.tmp` onto a directory throws, and both exceptions it can throw map to the
settings-error title naming the path. So: back the file up, remove it,
`New-Item -ItemType Directory ~/.pisum-whisper.json`, launch Release, observe, remove the directory
and the leftover `.tmp`, restore. Denying write on the profile directory instead would touch every
application on the machine and is hard to undo exactly.

**`ValidateOnBuild`** is as written: comment out the `AddNativeOutput()` call in `Program.BuildHost`,
build Release, launch, expect the "could not start" title, `git checkout -- src/Pisum.Whisper.App/Program.cs`,
rebuild. All three end with `git status` clean, and none of the three edits is ever committed.

Each row records the dialog's title and whether it names the file, the exit code, the `Fatal` line,
and — for the file cases — that the file or directory was left as it was found.

### 7. The login-time dialog is the one check that ends the session

Sequence, all against the Release build: launch it, make sure *Start with system* is on in the
General tab, confirm the `Run` value names the Release executable, Quit. Corrupt the settings file to
the same `{ "startWithSystem": true, "hotkey": { BROKEN` the first run used — the backup exists.
Sign out, sign in, wait, and note whether the Settings Error dialog is in front of or behind whatever
else login brings up, and what that was. Dismiss it, restore the file, confirm the log's `Fatal` line
carries the login timestamp, and launch once more so the reconciler aligns the `Run` value with the
restored file; confirm it reads as it did before the checks, toggling the switch if the recorded path
differs. It runs last, after everything else is recorded and committed, because the sign-out takes
the terminal and the IDE with it; the observation is noted by hand and written up afterwards.

### 8. Rider discovery is already answered by Rider's own logs, and only the gutter click is left

Rider's unit-test session logs (`%LOCALAPPDATA%\JetBrains\Rider2026.2\log\UnitTestLogs\Sessions`)
hold two runs that settle the discovery half before anyone opens the window. On 2026-08-31 at 17:11,
the day of the migration, a solution-wide session ran `Pisum.Whisper.Core.Tests` and
`Pisum.Whisper.Platform.Tests` through the xUnit provider — 403 elements, and runtime discovery
reported `(+0 ~0 -0)`, so the statically discovered tree matched what the runner found exactly. On
2026-09-02 at 10:11 a session scoped to `Pisum.Whisper.App.Tests` ran under dotCover with
`Provider: xUnit`, `Strategy: XUnitTestRunnerRunStrategy`, `net10.0`: "Got 147 elements to run",
which is that project's 127 `[Fact]`, `[AvaloniaFact]` and `[Theory]` attributes plus its 20 test
classes, to the element; 46 theory rows were added at runtime and the run finished in 20 s. That
second run was triggered from a Claude Code session by the Rider MCP's `findTests` tool, which builds
and runs tests to compute coverage — a side effect worth knowing about before calling it again.

So Rider discovers all three projects under the pinned xUnit v3 and Microsoft.Testing.Platform
setup and runs them; the VSTest-bridge fallback the migration design priced is moot and is not taken.
What 5.4 still owes a person is the part only a person can do: one test run from the gutter, and a
glance that the Unit Tests window's total is the same number `dotnet test Pisum.Whisper.slnx` prints
on the same commit — the command is the authority, not `CLAUDE.md`'s 620, which drifts. Should the
window somehow disagree with its own logs, Rider's Testing Platform support under *Settings → Build,
Execution, Deployment → Unit Testing* is the first thing to check, and the fallback stays where the
migration design left it, as a separate decision rather than a step of this change.

### 9. Clipboard history is switched on for the check and back off after

The 2026-08-31 run could not perform 3.3 because `EnableClipboardHistory` was unset and Win+V
retained nothing. It is turned on in *Settings → System → Clipboard* for the check, and *Sync across
devices* is left off — so the cloud half of the exclusion stays unverified here and the record says
so. Dictate into Notepad, open Win+V, confirm the transcript is absent and an ordinary Ctrl+C from
Notepad is present, and that the restore of the previous clipboard did not add a second entry, which
is the spec's second scenario. Then clear the history and turn it back off.

### 10. The commit delay is judged on three observations, and moves without a spec change

With `logLevel` at `debug`: paste an API key and see exactly one `Committing 1 settings changes.`;
type a character and press Tab within a fraction of a second, and find the edit in the file once the
window has elapsed; and note that nothing is perceptible as lag, since no view waits on the commit.
If the constant moves, it is `CommitDelay`, its doc comment and `CLAUDE.md`'s "400 ms" — the
`settings-window` spec names no number. The tests inject the delay and need no change.

### Windows and macOS

Everything here runs on Windows, and the macOS half of every check is issue #31. The two spec deltas
are platform-neutral. The pooled-thread case the `notifications` requirement describes is the same on
macOS, and is in fact the row change 11's risk table names — the hotkey grant refused at
`host.Start()` — so its macOS verification belongs with #31's pass. The budget is a network property,
not a platform one, and the number decided here holds on both.

### Rejected alternatives

- **Waiting for the Apple Silicon sitting** — what has kept these sitting; #30 exists to split them.
- **A permanent headless test for the pre-initialisation call** — the assembly initialises Avalonia
  once and cannot express "before".
- **A stopwatch for the budget** — the log timestamps both ends and leaves a record.
- **Recording every result in this change's `design.md` only** — the answers would sit two
  directories from their questions; the table here indexes, the archived sections hold.
- **Debug builds for the fatal reproductions** — a console changes the case.
- **Fixing the alt-tab blemish here if found** — needs Win32 interop Avalonia does not expose.
- **Denying write on the profile directory** — machine-wide; the directory collision is exact.
- **Renaming the icon in code instead of moving the file** — an edit to `App.cs`, where a moved file
  reproduces "missing" literally.

## Risks / Trade-offs

- **User state is altered by seven of the checks.** → Decision 3: hash backup first, restore in the
  same task, and the change is not done while the hash differs.
- **A 600 s run is one sample of one connection on one day, and costs quota.** → Two runs and a
  control, date and model recorded, and a decision rule fixed before the measurement.
- **The 7.4 experiment was expected to wedge the process, and the reading says it cannot.** It
  fails fast at `InitializeUIThreadDispatcher`, inside `Main`'s catch. → If it does anything else,
  that is the finding; Task Manager covers a wedge regardless, and the edit is throwaway.
- **Signing out ends the session.** → The login check runs last, after the rest is committed.
- **Three throwaway source edits could reach a commit.** → `git status` clean before and after each;
  the tasks say so.
- **Editing under `archive/`.** → The 2026-09-02 commit set the precedent for `design.md`; the boxes
  are the same kind of record.
- **The Rider fallback rewrites the test stack** and three `CLAUDE.md` claims. → Taken only after the
  Testing Platform setting is checked, and the documentation task grows accordingly.
- **A moved constant desyncs a spec.** → The `dictation-pipeline` number rides in this change's
  delta; the `settings-window` spec carries none.
- **The `notifications` requirement is verified on one platform** and states behaviour on both. →
  Said in the record and in #31.

## Verification results

Decision 1's index: one row per check, naming the archived section that holds the record rather than
repeating it. Written 2026-09-03, which is later than the run — task 7.1 went unticked when this
change was archived, and this table is the part of it that was owed.

**Five of the eleven checks ran.** The change was archived anyway, which is the roadmap's rule
working as intended rather than a slip: an archived change does not certify that its capability was
verified on hardware.

| Check | Task | Ran | Record |
|---|---|---|---|
| 8 / 6.1 — a dictation end to end, hold and toggle | 2.1 | yes | `add-dictation-pipeline`'s *Verification results* |
| 9 / 4.1 — Release build, overflow placement, tooltip live-refresh | 2.2 | yes | `add-tray-icon`'s *Verification results* |
| 10 / 6.3 — the 400 ms commit delay | 2.3 | yes | `add-settings-window`'s *Verification results* |
| 10 / 6.4 — the hotkey recorder suspends matching | 2.4 | **no** | — |
| 7 / 3.3 — the transcript stays out of clipboard history | 2.5 | **no** | — |
| 8 / 6.3 — the transcription budget, measured | 3.1 | **no** | — |
| 11 / 7.1, and the alt-tab half of 7.4 | 4.1 | **no** | — |
| 11 / 7.4 — the blocking-dispatcher experiment | 4.2 | yes | `add-system-integration`'s *Verification results* |
| 11 / 7.4 — the `FromThread` readiness gate | 4.3 | yes | `add-system-integration`'s *Verification results* |
| `report-startup-failures` / 7.1 — unwritable settings, and the driver | 5.1 | yes | `report-startup-failures`'s *Verification results* |
| `report-startup-failures` / 7.1 — `ValidateOnBuild` | 5.2 | yes | `report-startup-failures`'s *Verification results* |
| `report-startup-failures` / 7.1 — missing tray asset | 5.3 | yes | `report-startup-failures`'s *Verification results* |
| `report-startup-failures` / 7.3 — the dialog at login | 5.4 | **no** | — |
| `migrate-tests-to-xunit-v3` / 5.4 — Rider still discovers the tests | 6.1 | **no** | — |

`report-startup-failures`' 7.1 is closed by rows 5.1 to 5.3 **plus** the corrupt-settings
reproduction, which ran earlier under commit `ec8fe69` and closed issue #20 — the run that Decision
1 cites as the "Record …" commit pattern this change deliberately did not follow.

**No constant moved.** Task 2.3 judged `SettingsEditor.CommitDelay` and left it at 400 ms; task 3.1,
which is the only thing that could have moved `GeminiHttpClient.Timeout` or
`DictationOrchestrator.DefaultTranscriptionBudget`, did not run. So 400 ms, 60 s and 120 s all
stand, and `CLAUDE.md` still agrees with the code — task 7.1's verification clause, checked
2026-09-03.

**The six unrun checks are not lost.** Each is still an unchecked box in the archived change that
asked it, and `ROADMAP.md`'s *Artifact status* table lists every one. That is why task 7.1's
instruction to drop the win-x64 entries from that table was **not** carried out: it was written
expecting all eleven to run, and dropping rows for checks that never happened would make the roadmap
claim work that nobody did. The table was recomputed instead, under issue #31's rollup, and is
accurate at ten open tasks across seven changes.

## Open Questions

Each is settled by a task of this change and struck through in the archived design that asked it.

1. **Is 60 s the right request timeout, and is 120 s the right budget?** Two questions now, see
   Decision 4: the timeout is the one that bites first, and the budget follows it — task 3.1.
2. **Is 400 ms the right commit delay?** — task 2.3.
3. ~~**Does a pooled-thread `Present` before initialisation bind the UI thread for good?**~~
   **Predicted yes, from the source at the tag, see Decision 5** — and predicted to be fatal at
   `InitializeUIThreadDispatcher`'s `VerifyAccess()` rather than a late toast or a wedge. Task 4.2
   runs the experiment to confirm it; task 4.3 is written on the expectation that it does.
4. **Is the toast in the alt-tab list?** — task 4.1. **Predicted absent, from Avalonia's source
   rather than a guess.** The 12.1.1 Win32 backend (`src/Windows/Avalonia.Win32/WindowImpl.cs` at
   that tag) sets `WS_EX_TOOLWINDOW` nowhere; `ShowInTaskbar = false` clears `WS_EX_APPWINDOW` and
   gives the window a hidden owner, `OffscreenParentWindow`, at handle creation — the toast's XAML
   sets the flag before `Show`, so it takes that path. The switcher's rule (Raymond Chen, *Which
   windows appear in the Alt+Tab list?*) walks up to the root owner, then down the last-active-popup
   chain until a visible window, and lists the window only if that walk lands back on it. The toast
   is shown `SW_SHOWNOACTIVATE` and is never active, so the hidden owner's last active popup is
   itself, the walk ends on a hidden window, and the toast is not listed: the textbook hidden-owner
   trick, by the same mechanism that removed the taskbar button. The glance confirms rather than
   probes, because the Windows 11 switcher is not literally that pseudocode. **The reading also found
   a case the archived design does not cover:** the exclusion holds only while the toast is never
   activated. A click activates it — there is no `WS_EX_NOACTIVATE`, and Avalonia exposes none — which
   moves focus off the user's editor, the cost `ShowActivated = false` protects against on show, and
   makes the toast the owner's last active popup, listed from then on. "No click handling" is about
   handlers, not activation. Task 4.1 clicks once and records both; a fix would need Win32 interop
   and is a proposal of its own.
5. **Is `MessageBoxW` in front at login?** — task 5.4.
6. ~~**Does Rider discover the tests under Microsoft.Testing.Platform?**~~ **Answered from Rider's
   own session logs, see Decision 8:** yes, for all three projects — 403 elements on 2026-08-31 with
   runtime discovery `(+0 ~0 -0)`, and 147 elements for `App.Tests` on 2026-09-02, equal to its 127
   test attributes plus 20 classes. Task 6.1 keeps the gutter click and the window-total glance.
7. **Do `CanIncludeInClipboardHistory` and `ExcludeClipboardContentFromMonitorProcessing` have their
   documented effect?** — task 2.5.
8. **Which exception does the directory collision raise, `IOException` or
   `UnauthorizedAccessException`?** Both map to the settings title; recorded for the record —
   task 5.1.
9. ~~**Does the tooltip refresh live when the active preset changes?**~~ **Yes, see change 9's
   Verification results.** Switching to "Transcribe EN" in the running Settings window updated the
   tray tooltip immediately, with no relaunch — the subscription task 3.2 wired in but could not
   exercise until change 10 existed now works end to end.
