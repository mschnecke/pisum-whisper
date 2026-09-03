# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Layout

```
Pisum.Whisper.slnx
├── src/Pisum.Whisper.Core        domain + orchestration; no platform or UI dependencies
├── src/Pisum.Whisper.Platform    the OS-specific surface (the clipboard and paste probes today)
├── src/Pisum.Whisper.App         Avalonia tray shell and the composition root
├── tests/Pisum.Whisper.Core.Tests
├── tests/Pisum.Whisper.Platform.Tests   native registration, and the manual clipboard round trip
└── tests/Pisum.Whisper.App.Tests        the settings window, on Avalonia.Headless.XUnit

spikes/Pisum.Whisper.Spikes       throwaway; NOT in the solution — see "Spikes" below
```

One `net10.0` target for every project, including `Platform`: everything OS-specific here is
P/Invoke or `Process.Start`, so runtime `OperatingSystem.IsWindows()` checks plus
`[SupportedOSPlatform]` are enough. Settings live in `Directory.Build.props` (nullable, implicit
usings, **warnings as errors**) and package versions in `Directory.Packages.props` (central package
management — a `PackageReference` must carry no `Version`).

`NuGet.config` clears inherited sources and pins nuget.org. Do not remove it: the usual developer
configuration here restores from private feeds that CI cannot reach, and two active sources trip
NU1507 under central package management.

## Build, test, run

```bash
dotnet build Pisum.Whisper.slnx           # must stay at 0 warnings — they are errors
dotnet test Pisum.Whisper.slnx
dotnet run --project src/Pisum.Whisper.App
```

The app is a **tray-only process**: no window, no taskbar button, no console. It stays alive on the
tray icon and exits only via Quit, so `dotnet run` will not return — that is correct behaviour, not
a hang. Every build writes to `~/.pisum-whisper/logs/pisum-whisper.log`, and a **debug** build also
echoes to the terminal it was launched from (a `WinExe` inherits console handles), where at the
default `info` level

```
[10:09:56 INF] Settings loaded from C:\Users\you\.pisum-whisper.json (first launch: False).
```

is the sign it came up. A release build prints nothing to the console at all, and
`[10:09:57 DBG] Service container built and resolved; initialising the tray icon.` appears only with
`logLevel` at `debug`.

Per-runtime builds must name a project, not the solution — `dotnet build -r <rid>` against a `.slnx`
fails with `NETSDK1134`:

```bash
dotnet build src/Pisum.Whisper.App -r win-x64
dotnet build src/Pisum.Whisper.App -r osx-arm64      # cross-builds fine from Windows
```

There is no lint or format step configured. Warnings-as-errors is the whole quality gate today.

## The test stack

**xUnit v3, on Microsoft.Testing.Platform, running in parallel.** Five things follow from that which
the code does not show you.

**`dotnet test` is the MTP command, not VSTest, and `global.json` is why.** It carries a `test` block
next to the `sdk` block; with it, each test assembly runs through its own apphost and nothing hosts
it, which is why there is no `Microsoft.NET.Test.Sdk` reference. The cost is the CLI surface:
**`--filter <expr>` is VSTest syntax and does not work.** The replacements are `--filter-method`,
`--filter-class` and `--filter-namespace`, all of which take `*` wildcards:

```bash
dotnet test Pisum.Whisper.slnx
dotnet test tests/Pisum.Whisper.Core.Tests --filter-namespace Pisum.Whisper.Core.Tests.Hotkeys
```

`xunit.v3` is pinned to **3.2.2, not the current 4.0.0**, because that is what
`Avalonia.Headless.XUnit` 12.1.1 is compiled against — 4.0.0 resolves silently against a major it was
not built for, and the breakage would land in the settings window rather than here.

**A test that cannot run somewhere declares itself skipped; it is never filtered out by the runner
invocation.** That is the rule `ready-the-suite-for-ci` settled, and it is why the CI command carries
exactly one filter and will only ever need one. A skip prints its reason in the runner output beside
the test; a `--filter-not-method` in a workflow file prints nothing at all and silently drops the
count by one. There are **three gates**, all the same shape — an `internal static` class with an
`Enabled` property, named in `SkipUnless` and `SkipType`:

| gate | opts in with | what it holds |
|---|---|---|
| `ManualTests` | `PISUM_WHISPER_RUN_MANUAL` | 4 tests needing a real mic, clipboard, keyboard or API key |
| `WindowsOnly` | *(the running OS)* | 5 `WindowsAutostartTests`, which reach the registry |
| `TimingTests` | `PISUM_WHISPER_RUN_TIMING` | 1 test whose p99.9 latency bound measures the machine |

So a macOS run reports 6 skipped and a Windows run 1, each with its reason, and neither needs a flag.

**The four manual tests need an environment variable, because a skipped test cannot be run.** xUnit
has no runner option for it: `-explicit` covers explicit tests only. So `ManualCaptureSmokeTest`,
`ManualTranscriptionSmokeTest`, `ManualClipboardRoundTrip` and `ManualDictationSmokeTest` are gated on
`ManualTests.Enabled` via `SkipUnless` — they report skipped with their reason by default, and run
when `PISUM_WHISPER_RUN_MANUAL` is set. Verified on Windows:

```bash
dotnet test tests/Pisum.Whisper.Platform.Tests \
  --filter-method '*ManualClipboardRoundTrip.ATokenSurvivesAWriteAndAReadBack' \
  -e PISUM_WHISPER_RUN_MANUAL=1
```

Run them one at a time. They contend for the one real clipboard and the one real microphone, so
letting two run beside the suite is how you get a failure that means nothing.

**Tests run in parallel by class, and nothing in the suite shares mutable state to protect** — no
statics, no environment mutation, and every fixture builds its own
`Path.Combine(Path.GetTempPath(), "pisum-whisper-tests", Guid.NewGuid().ToString("n"))`. Do not add
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`; if a new test needs isolation,
isolate that class. The one that does is `FileLoggingRotationTests`, which sits in a
`DisableParallelization` collection because one of its five tests,
`WritesDoNotStallTheCallingThreadWhenTheFileRolls`, asserts a p99.9 write latency under 500 µs and
therefore measures the machine as much as the code. **That one test is now gated on `TimingTests`**
and reports skipped; the other four still run, which is what the collection is for. Serialising was
not enough on its own — it was still occasionally over the bound — and the bound is `file-logging`'s
to set, so `ready-the-suite-for-ci` gated it rather than relaxing or deleting it. The flakiness is a
**Windows** measurement, roughly three failures in twenty-two runs on the developer machine; it
failed 0 of 38 runs on Apple Silicon, six of them under deliberate CPU starvation. If it turns out
not to flake on Windows either, the honest follow-up is removing the gate. The
other thing parallelism exposed is subtler and worth copying: **`SimpleGlobalHook.IsRunning` is not a
readiness signal.** It turns true before the provider's dispatch proc is installed, and an event
posted in that window is answered with `Success` and then dropped for good — no later wait recovers
it. Wait for the hook's `HookEnabled` event instead, which is delivered through the dispatch proc and
so cannot precede one. `HookProviderProbeTests` does that, and then waits for its own handler before
`Stop()`, which does not drain what is in flight. Measured under a starved thread pool, `IsRunning`
loses this 2 times in 3000 and `HookEnabled` none; on an idle machine neither loses, which is why
this arrived only once tests ran in parallel.

**Every test class carries a category trait — `[Trait(Traits.Category, Traits.Categories.Unit)]`
and its `Integration` and `Manual` siblings — and the value is decided by what the test touches, not
by where it lives.** The names are string constants in `Traits.cs` in each test project, applied
through xUnit's own `[Trait]`; there is no custom attribute, because `TraitAttribute` is sealed and
the `Xunit.v3.ITraitAttribute` implementation it would take is more machinery than a pair of
constants. The runner sees an ordinary `Category` trait either way, so the filters below are the
normal ones. `Integration` means running it creates a real file or directory
under the temp path, or builds a real DI container or generic `Host` — following the base-class chain,
which is why every class deriving `DictationTestBase`, `FileLoggingTestBase` or
`GlobalHotkeyServiceTestBase` is one: those bases create a temp home in their constructor.
`Unit` means neither; in-memory objects and fakes only, including the Gemini tests, which drive a
real `HttpClient` over a fake handler and never reach the network. The split is 28 / 55 / 4 classes and
224 / 401 / 4 tests — they sum to 629, so exactly one category applies to every test.

```bash
dotnet test Pisum.Whisper.slnx --filter-trait Category=Unit          # 224, no I/O at all
dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual    # 625, what CI runs
```

Keep the rule mechanical when adding a class: if its constructor or its base's reaches
`Path.GetTempPath`, `Directory.CreateDirectory`, `File.WriteAll*`, `new ServiceCollection` or
`Host.CreateApplicationBuilder`, **or writes to the real registry**, it is `Integration`.
`WindowsAutostartTests` is the one class the last clause is for: it round-trips a private `HKCU`
subkey it deletes in `Dispose`, which is neither a temp file nor a container and is plainly not
`Unit`. `TextOutputTestBase` is the one base that
is not — it builds a fake clipboard, a fake probe and a `TestProvider`, all in memory — so its four
derived classes are `Unit`.

**`[TestCleanup]` is `Dispose()` now.** MSTest and xUnit agree on a fresh instance per test method, so
a lifecycle pair is a constructor and `IDisposable` — there are fourteen, and four of them are on
abstract bases whose derived classes add nothing. Assertions are Shouldly throughout; `xunit.v3.assert`
comes along with the framework but is unused.

## Spikes

`spikes/Pisum.Whisper.Spikes` is deliberately outside the solution, so that a harness can be
**re-run rather than re-written**. It was originally kept for the macOS verification tracked by issue
#15, which closed on 2026-08-28 — but **it is not deletable**, and change 11 is what made that true
again. Two of its rows are held by work that has not happened:

- `toast` (S7) is the measurement the notification transport was chosen on, and change 11's task 7.3
  requires it to be re-run on Apple Silicon. It is written to run there unchanged.
- `notify` (S6) stays in the tree **unrun**. Its three observational questions matter again the day
  change 12's Start-menu shortcut carries an AUMID, and the control run recorded in change 11's
  `design.md` — a WinForms balloon beside it — is the experiment that would settle them.

`fatal` (added by `settle-win-x64-verification-debt`'s task 5.1) is a third holder, and a different
kind: it is not a question but a **driver**. It launches an executable, waits for a top-level window
of that process whose title matches, posts `WM_CLOSE` — which an `MB_OK` box answers as OK — waits
for exit, and prints the exit code and the newest `[FTL]` line from the log. It is Windows-only and
says so when run elsewhere. It exists because the four fatal-startup reproductions share that
launch-observe-dismiss half, and it was kept rather than left in a scratchpad so change 12's CI can
run it. The state setup each reproduction needs — moving a settings file aside, commenting out a
registration, moving a tray asset — stays by hand.

Deleting the harness is a decision for whoever no longer wants those three runs, and the two macOS
`FAIL` rows, re-runnable — not an oversight.

```bash
dotnet run --project spikes/Pisum.Whisper.Spikes -- hook       # global hook, both key edges
dotnet run --project spikes/Pisum.Whisper.Spikes -- paste      # simulated Ctrl+V into Notepad
dotnet run --project spikes/Pisum.Whisper.Spikes -- audio      # capture format and rate conversion
dotnet run --project spikes/Pisum.Whisper.Spikes -- opus       # Ogg/Opus encode + decode round trip
dotnet run --project spikes/Pisum.Whisper.Spikes -- tray       # tray icon, tooltip, runtime swap
dotnet run --project spikes/Pisum.Whisper.Spikes -- notify     # Shell_NotifyIcon balloons, three trials
dotnet run --project spikes/Pisum.Whisper.Spikes -- toast      # an application-drawn notification, measured
dotnet run --project spikes/Pisum.Whisper.Spikes -- combined   # hook + Avalonia run loop together
dotnet run --project spikes/Pisum.Whisper.Spikes -- fatal <exe> <title>   # a fatal-startup repro
dotnet run --project spikes/Pisum.Whisper.Spikes -- api <assembly> [filter]
```

`opus` consumes what `audio` writes, so run `audio` first. `combined` is the one to run **first on a
Mac**: it is the shape changes 6, 8 and 9 all take, and the macOS run-loop question is the highest
open risk in the project. Results so far are recorded in
`openspec/changes/archive/2026-08-27-bootstrap-solution/design.md` under *Windows spike results* and
the *Platform verification* matrix; `api` is a reflection dumper for exploring package surfaces.

## What this is

Hotkey-driven dictation: hold a global hotkey to record speech, release to transcribe it via AI,
and the transcript is pasted at the cursor position.

```
global hotkey (hold or toggle) -> mic capture -> Opus/WAV encode
  -> Gemini upload with the active preset's system prompt
  -> clipboard + synthetic Ctrl+V / Cmd+V at the cursor
```

Targets Windows x64 and macOS Apple Silicon. Cloud-only and Gemini-only: **local Whisper inference
is out of scope despite the repository name.** It is a re-creation of `W:\github-pisum-transcript`
(Tauri 2 + Svelte 5), which is the behavioural specification — wire formats, the recording state
machine and its timing constants, the settings schema and the error taxonomy all come from it. None
of its code transfers; it is read and re-expressed. The one deliberate divergence is the two
built-in preset prompts: change 2 rewrote them and `BuiltinPresets.cs` owns them now, so do not
re-sync them from the reference's `config/presets.rs`.

## Stack

.NET 10 (`net10.0`) on the `10.0.400` SDK, developed in JetBrains Rider on Windows. Avalonia 12.1
for the tray and, later, the settings window. SharpHook for the global hook and the paste
simulation, SoundFlow (miniaudio) for capture, Concentus plus `Concentus.Oggfile` for Ogg/Opus,
Serilog for file logging, Google Gemini for transcription. Every version is pinned in
`Directory.Packages.props`.

The three risky dependencies — global key **release**, cross-platform capture, a macOS menu-bar icon
— were spiked in change 1 and pass on Windows. Issue #15 re-ran them on an Apple M4 (macOS 26.6.2)
and all three pass there too; what came back **FAIL** is the synthetic paste into a foreign app and
the Accessibility grant surviving a rebuild. Both are in the *Platform verification* matrix in
`openspec/changes/archive/2026-08-27-bootstrap-solution/design.md`, each with its fallback.

## Logging

Everything logs through `ILogger<T>`. Serilog is the implementation, registered by `AddFileLogging`
in `Core/Logging/` from `Program.cs` before the container is built, so a `ValidateOnBuild` failure
reaches the file rather than a console the release build does not have. Output rolls by size and is
swept by age; the console sink is `#if DEBUG` only.

`logLevel` in settings takes effect immediately, through a `LoggingLevelSwitch`. **Never add a
`SetMinimumLevel` call**: `AddSerilog` installs a provider-scoped filter at `Trace`, so
`Microsoft.Extensions.Logging` does not gate Serilog, and a minimum level put back would be a second
gate in front of the switch that silently breaks the runtime level change.

Six rules every later change is written against:

- **Never log transcript text or API key values.** Transcripts are the user's speech and the settings
  file holds API keys. Log lengths, categories and outcomes instead — the character count, not the
  characters.
- **Never log clipboard contents.** `Core/Output/` reads what the user had copied before it writes a
  transcript over it, and a password manager's clipboard is the obvious thing to find there — so
  those contents are not logged at any level, not even by length. What `TextOutput` may write down
  is the transcript's character count, the paste result, and which guard stood a restore down.
- **Never log a keystroke that is not the configured hotkey.** `Core/Hotkeys/` observes every key on
  the machine in order to match one combination, so a `Trace` statement dumping `e.Data.KeyCode` —
  the obvious thing to write while debugging it — turns the log file into a keylog, one that change
  10's "Open Log Folder" button then puts a click away. The binding, its edges, counts and outcomes
  are loggable; nothing else about a key is, at any level.
- **No `IsEnabled` guard on hot-path statements.** A suppressed call costs about 0.1 µs, which is less
  than the guard, so the trace statements in the audio path are written plain.
- **Never use Serilog's static `Log`.** It is in scope project-wide because `Core` references Serilog,
  but the configured logger is always passed explicitly.
- **Never put a transcript, an API key or clipboard contents in a notification either.** This is the
  three disclosure rules above applied to a surface that is wider, not narrower: what
  `Core/Notifications/` is handed is drawn over whatever the user is presenting or screen-sharing,
  where the log file at least needs opening. What the six call sites may say is the title and message `DictationFailure.Describe`
  produces — safe because `TranscriptionException.Message` embeds Google's error body and
  `IsRetryable`'s status-before-body check guarantees that is never a transcript, and because the key
  travels in a header rather than the query string. `DictationNotificationTests` asserts it on a
  failure path where a transcript genuinely exists, because a rule written down is not a verification.

## The global hotkey

`Core/Hotkeys/` owns the one global keyboard hook this process is allowed to run — libuiohook keeps a
single static callback, so a second concurrent hook corrupts its internal state. `AddGlobalHotkey`
registers `GlobalHotkeyService` once and resolves it as the service, the contract and the hosted
service; change 10's hotkey recorder reuses it through `CaptureAsync` rather than building its own.

**Nothing but matching runs on the hook thread.** Windows removes a low-level hook that exceeds
`LowLevelHooksTimeout` — 1000 ms by default — with no exception and no event, and macOS disables an
event tap that stops responding. The handlers match, set `SuppressEvent` and write one enum to a
channel; a dispatch loop raises the events, so a consumer that takes a second to open a microphone
cannot cost the user their hotkey. Never add work to a hook handler, and never raise an event
directly from one.

**Startup is bounded, because a missing grant does not fail.** On macOS an absent Accessibility
grant neither throws nor prompts: change 1's spike found libuiohook blocking for ever at zero CPU
with the tap never installed. `StartAsync` therefore races the hook against a five second timeout
and, on losing, records `HotkeyAvailability.Failed`, says so in the log and lets the process come up
without a hotkey. A hook that enables *after* that timeout still reports itself, so the timeout
bounds the waiting rather than settling the verdict. This is not a recovery path for a late grant:
macOS does not reliably deliver one to a running process, so there is deliberately **no retry
loop** and the user relaunches. Keep it that way — a start that blocks here has no window to explain
itself from.

Two consequences worth knowing before touching the matcher: `SharpHook.Data.EventMask`'s group values
are unions of both sides (`Ctrl` is `LeftCtrl | RightCtrl`), so `HasFlag` demands both keys at once;
and the mask also carries the lock keys and the mouse buttons. `ModifierGroups.FromEventMask` folds
those away, and matching compares the folded groups for equality.

## Transcription

`Core/Transcription/` sends the encoded audio to Gemini and returns the text. `AddGeminiTranscription`
registers **`GeminiProviderPool` as the `ITranscriptionProvider`**, and `GeminiProvider` — one key and
one model — is `internal` behind it, so change 8's pipeline depends on a single contract and never
learns how many keys are configured.

**The API key travels in the `x-goog-api-key` header and must not move to the query string.** The
reference uses `?key=`; `IHttpClientFactory` logs every request URI at `Information` and the default
`logLevel` is `info`, so the query form would write the user's key into the log file that change 10
puts one click away. For the same reason nothing in `SendWithRetryAsync` logs the request message or
its headers, and `GeminiKeyProbe` scrubs the key out of any message it re-throws.

**The pool is never rebuilt.** It reads the enabled entries from `SettingsStore.Current` on each call
and snapshots them once, so a save mid-transcription cannot change the set between fallback attempts.
That is a deliberate divergence: the reference copies settings into a global pool in `apply_settings`
because it has no authoritative in-memory store, and this codebase has one. No rebuild step, no change
subscription and no lock — the only durable state is the round-robin cursor.

**`IsRetryable` checks the status before it looks at the body**, which is the one place this capability
corrects the reference rather than reproducing it. The body is matched for "overloaded", "too many
requests" and "rate limit" — and on a 200 the body *is* the transcript, so without the success check a
user dictating "we hit the rate limit yesterday" would have their speech retried three times and then
fail. `FailureFor` is the mirror of that rule: it embeds up to 200 characters of the body in its
message, which is safe **only** because it is called for unsuccessful responses, where the body is
Google's error JSON and never a transcript.

Failures carry an `ErrorCategory` — `Configuration`, `Network`, `Authentication`, `RateLimit`,
`Transcription` — fixed where the failure is raised rather than re-derived from message text by the
caller. When every provider fails the pool aggregates them and **a category they all share survives**:
a single misconfigured key must still reach the user as an authentication failure instead of being
flattened into a generic one. Mixed categories do flatten, to `Transcription`.

Three constants that are provider knowledge rather than pipeline knowledge: the **14 MiB inline
ceiling** is checked in `GeminiProvider`, so an oversized recording fails once instead of once per
configured key; the **60 s timeout** on the named client is per request, not per transcription, because
a budget spanning retries and providers belongs to change 8 through the token it already passes; and
retries are **three attempts and two waits**, 1 s then 2 s, injected as a delegate so the tests do not
spend three real seconds. `GeminiKeyProbe` deliberately retries neither of its calls — both are started
by a user looking at a window they can click again, unlike a dictation already spoken.

## Text output

`Core/Output/` owns the whole delivery — read the clipboard's previous text, write the transcript,
paste, restore — behind one `ITextOutput.DeliverAsync`. The steps are invariants about each other
("never restore after a failed paste", "only restore what is still ours"), not a sequence a caller
may order, so they do not split into thinner services for change 8 to sequence.

**The clipboard is native code in `Pisum.Whisper.Platform`, and that is deliberate.** Avalonia cannot
supply one to this process: in 12.1 `Avalonia.Application` has no `Clipboard` property, `TopLevel.Clipboard`
is the only public route to an `IClipboard`, the concrete `Avalonia.Input.Platform.Clipboard` is not
exported — and this is a tray-only process that creates no `TopLevel` at all. So `ISystemClipboard`
and `IPasteProbe` are declared in `Core` and implemented over Win32 and `NSPasteboard` in
`Platform/Output/`, which is the first and so far only code in that project. Do not replace it with
an Avalonia clipboard; there isn't one to reach. Win32 is also the better owner regardless:
`SetClipboardData` hands the data to the system, so a transcript left on the clipboard for a manual
paste survives this process exiting, where Avalonia's OLE path would not.

`AddTextOutput` (Core) and `AddNativeOutput` (Platform) are registered separately on purpose — with
`ValidateOnBuild` on, omitting the native half is a startup failure naming `ISystemClipboard` rather
than a null reference at the first paste.

Three things not to undo: the paste keystroke is paced 30 ms per edge **on macOS only** (edges posted
back to back outrun the OS folding earlier keys into the modifier flags, and Cmd+V arrives as a bare
"v"); `TextOutput` must never be called from a hook handler — it sleeps for over a second, which is
exactly what gets a low-level hook removed; and `MacOsClipboard` wraps both of its operations in an
`objc_autoreleasePoolPush`/`Pop` pair, because every object it touches arrives autoreleased and this
runs on a thread-pool thread rather than inside an AppKit callback, where the run loop would drain a
pool for it.

## The dictation pipeline

`Core/Dictation/` is the recording state machine — the component that turns the hotkey's two edges
into a recording and a recording into text at the cursor. `AddDictationPipeline` registers
`DictationOrchestrator` once, as both the singleton and a hosted service; it is a concrete class with
no interface, following `SettingsStore` rather than `IGlobalHotkeyService`, because every dependency
it has is already a seam.

**Nothing but a state transition may run in a hotkey handler.** `GlobalHotkeyService` raises its
events *synchronously* from its channel read loop, so a handler that awaits the pipeline blocks that
loop — and in hold-to-record the very next thing in the channel is the release that ends the
recording. The handlers claim a transition under one lock and return; everything with a duration runs
on a pooled task. This is separate from, and additional to, the rule about the hook thread itself.

**The state is three values, not a boolean.** `Idle`, `Recording`, `Transcribing`. The reference
publishes one flag and clears it only after the paste, so its icon claims to be recording throughout
the upload. Publishing three is the *smaller* change here, not the larger one: the orchestrator must
already tell recording from transcribing because a hotkey press means different things in each, so a
boolean would mean adding a step that discards what is known. Announcements are made by the pipeline
task itself, as its first act — announcing from the claiming thread lets a fast dictation's `Idle`
overtake its own `Transcribing` and leaves a subscriber stuck.

**A transcription is bounded by a 120 s budget, and the per-request timeout is not that bound.** The
60 s on the Gemini client is per request; three attempts across N keys multiply it to 183 s per key,
so six minutes with two configured — throughout which the hotkey does nothing and says nothing. The
budget is a linked token wrapping `TranscribeAsync` **only**. `DeliverAsync` gets the shutdown token
alone, because it spends more than a second waiting before its restore by design and an expired
transcription clock must not cut that short. It is a constant, deliberately not a setting.

**`StopAsync` claims, cancels *and awaits*.** It is the fourth way a recording can end, alongside the
release edge, a toggle press and the watchdog, so it takes the same atomic claim they do — removing
the handlers does not retract an event invocation already in flight, and `MiniAudioCapture.StopAsync`
is not reentrant. Awaiting is a correctness requirement, not tidiness: between
`TextOutput` writing the transcript and restoring the previous clipboard, those previous contents
exist nowhere but inside that call, and on Windows `SetClipboardData` hands ownership to the system,
so the transcript outlives the process. Cancelling without awaiting lets the process exit inside that
window and destroys the user's clipboard permanently.

**Two rules that look redundant and are not.** The 50 ms minimum and the empty-capture check are a
diagnosis, not a duplicate: under 50 ms is a brush and is discarded in silence, while over 50 ms with
no samples is a dead microphone and is reported. Merging them into one sample-count measurement would
silently swallow every dictation from a muted input device. And the 200 ms toggle debounce is
**kept for a different reason than the reference gives** — its stated purpose is auto-repeat, which
`HotkeyMatcher` already absorbs without raising an edge; what it still covers is a fumbled double-tap
between 50 ms and 200 ms, which would otherwise be uploaded and fail. Do not delete it as dead code.

**Failures are described, never matched.** `DictationFailure.Describe` maps an exception to a title
and a message by type for `AudioException`, `TextOutputException` and `OperationCanceledException`,
and by `ErrorCategory` for `TranscriptionException`. There is no substring matching on message text,
and the reference's macOS "Microphone Access Required" branch is deliberately absent — it needs
exactly that, and spike S2 passed with the microphone accessible, so nobody has observed what a
refused grant looks like. Change 11 adds the notification transport and the
forced-versus-suppressible policy and calls the same function; it **modifies** this capability rather
than filling in markers left for it.

**The pipeline task catches everything.** An exception escaping it becomes an unobserved task
exception, which does not crash the process — it vanishes, leaves the state at `Transcribing` for
ever, and the hotkey answers "Transcription In Progress" until the application is restarted. The
`finally` is there for the state reset first and the message second.

**The watchdog task is not inside that catch, and its notification is guarded for the same reason
`Announce`'s subscriber is.** `ArmWatchdog` spawns its own `Task.Run` whose only `try` covers the
delay, so the log-notify-`TryStopRecording` sequence after it runs unprotected: an escaping throw
from the presenter would skip the stop entirely, leaving the recording running past its maximum with
the watchdog already spent, and surface as an unobserved task exception rather than a failure.
Telling the user is the optional half of that task; stopping the recording is not, so the notify
carries its own `catch` and the stop follows it unconditionally. Anything added between the delay and
`TryStopRecording` needs the same treatment. `DictationWatchdogTests.APresenterThatThrowsStillStopsTheRecording`
is the guard, and it fails without the `catch` rather than merely covering it.

## The tray icon

`src/Pisum.Whisper.App/App.cs` is the whole capability — no type in `Core`, no `ITrayPresenter`. The
mapping is a switch over three enum values and one string interpolation, and this file is its only
consumer.

**There are three icons because there are three `DictationState` values.** Collapsing them
reproduces the reference's defect either way round: `Transcribing → recording` is precisely what
`tray::set_recording_state(false)` after the paste does, an icon claiming to record throughout the
upload, while `Transcribing → idle` tells a user in toggle mode that nothing is happening right
before the hotkey answers "Transcription In Progress". The third value costs nothing to publish —
the orchestrator must already tell the two apart to interpret a press — so collapsing it here would
be work spent discarding what is known. The map is an exhaustive `switch` expression so a fourth
state fails the build; it carries a local `#pragma warning disable CS8524` for the *unnamed* enum
value, and **must not** be given a `_ =>` arm instead: that clears `CS8509` too and moves the fourth
state from a compile error to a runtime one.

**There is no theme handling on either platform** — no probe, no `ColorValuesChanged`, no light/dark
variants. macOS delegates to AppKit through `MacOSProperties.SetIsTemplateIcon(trayIcon, true)`;
note the owning type, because `TrayIcon` has no such member and looking there and finding nothing
reads as "Avalonia does not expose this", which is the trap an earlier draft of the design fell into.
Windows carries the contrast in the art instead: every theme value Avalonia can reach reports the
*apps* theme, while the Windows 11 taskbar follows `SystemUsesLightTheme`, so neither available route
picks the right background under "Custom" mode.

**Every interior mark is a hole, not a light-coloured fill.** A template image is rendered from its
alpha channel alone, so white inside the bubble vanishes and a black dot overlapping the bubble
merges into the same silhouette — both are mistakes the reference's shipped assets actually make.
Drawing the marks as holes is also what lets one geometry serve both platforms and what removes the
Windows theme probe, since a hole reads against the bubble whatever is behind it.

**`host.StopAsync` runs after the Avalonia loop has returned**, so the tray's release line is *not*
the last thing in the log — `DictationOrchestrator.StopAsync`'s lines, the clipboard restore among
them, follow it, and that is change 8's `StopAsync`-awaits requirement working rather than a leak.
The corollary is that `OnExit`'s unsubscribe is cheap insurance, not the invariant it resembles: a
shutdown `Idle` is posted into a dispatcher nobody pumps and never arrives at all. What actually
covers the narrow case — a pipeline task announcing between the Quit click and the loop stopping —
is `_trayIcon = null` and the null check in the handler, so keep both.

Icons are loaded once in the constructor, not per state change, and both event subscriptions are
marshalled through `Dispatcher.UIThread.Post`, which preserves order at equal priority so a fast
dictation's `Idle` cannot overtake its own `Transcribing`. The tooltip names the active preset and
**never** its `SystemPrompt`.

**`App/Assets/` holds four files per state and only one of them is loaded.** The runtime pair is
`tray-<state>.png` and `tray-<state>Template.png`, chosen by a single `OperatingSystem.IsMacOS()`
that appends the `Template` suffix for the whole set at once — that suffix is AppKit's own naming
convention, so keep it. Beside each pair sit the `.win.svg` and `.mac.svg` the PNGs are exported
from; they are the editable source and nothing reads them at runtime, which is why
`Pisum.Whisper.App.csproj` follows its `<AvaloniaResource Include="Assets\**" />` with a `Remove`
for `Assets\*.svg`. Edit the SVG, re-export the PNG. Export both halves of a pair: only the running
platform's variant is opened, so a forgotten `Template` export leaves Windows building and passing
while macOS throws out of `AssetLoader.Open` in the constructor — on hardware a Windows machine
cannot reach.

## The settings window

`src/Pisum.Whisper.App/Settings/` is the whole capability — the window, one `SettingsEditor`, six
views and nine view models. Nothing of it is in `Core`, following change 9's precedent; the two
exceptions are contracts rather than presentation, `Core/Shell/ISystemShell.cs` and
`Core/Transcription/GeminiDefaults.cs`.

**The live-apply fan-out already existed, and this change adds no re-application step.** Change 3
hot-swaps the log level, change 6 rebinds the hotkey matcher, change 9 refreshes the tray tooltip,
and `GeminiProviderPool` and `DictationOrchestrator` read `SettingsStore.Current` per use. All of it
was dead code until this window became the first runtime caller of `Save`. In particular **the
provider pool is still never rebuilt**: `TranscribeAsync` snapshots the enabled entries once per call
so a save mid-transcription cannot change the set between fallback attempts, and a rebuild driven
from `Changed` would reintroduce exactly that race.

**Edits go to a clone and are written after 400 ms of quiet.** `SettingsEditor.Edit` applies the
change to a draft taken from `SettingsStore.CloneCurrent()` and restarts a timer; the commit calls
`Save`. Binding the views to `SettingsStore.Current` instead would be simpler and would make a
half-typed API key visible to a dictation already in flight, because the pool reads `Current` at
transcribe time — the user would authenticate with the prefix. It also costs one file write per
pasted key rather than 39, and one `Changed` rather than 39. The delay is injected, so no test waits
400 ms.

**The clone forces an edit-by-id invariant, and breaking it fails silently.** `Save` assigns
`Current = draft`, so the graph is replaced on every commit: a delegate that closes over a
`ProviderConfig` or a `Preset` writes into a graph nothing will ever save, and the edit vanishes with
no exception. Every delegate looks its target up inside the `AppSettings` it is handed, with
`FirstOrDefault` and **never** `First` — a removal and a keystroke can land in the same quiet window,
and a `First` there throws on the pooled commit thread as an unobserved task exception.

**Every Presets command awaits `FlushAsync` first.** That tab writes through
`SettingsStore.SavePreset`, `DeletePreset` and `SetActivePreset` rather than through the editor,
because those encode rules the view model must not duplicate. They also replace `Current`, so a draft
cloned *before* a preset was added would be saved *after* it and would silently delete it. One line
per command, one test per command. `SettingsEditorTests.AStaleDraftNeverRevertsAPresetWrittenThroughTheStore`
is the regression guard for the whole clone decision.

**A write that never reaches disk is now reported, not swallowed.** Neither `SettingsEditor.Commit()`
nor any of the four `PresetsViewModel` commands used to catch anything: an `IAsyncRelayCommand`'s
`ICommand.Execute` does not await its `Task`, and `Commit()` itself runs unawaited on a pooled thread,
so a `SettingsException` from `SettingsStore.Write` — disk full, permission denied, a network drive
gone from under the home directory — became an unobserved task exception and the window looked like
the save had worked. `Commit()` now wraps its one `_store.Save(draft)` call in
`try`/`catch (SettingsException)`, logs at `Error` and calls
`INotificationService.Notify("Settings Not Saved", exception.Message)` — forced, ignoring
`showTrayNotifications`, because a save that silently failed is not chatter. `exception.Message` is
safe to show verbatim: it is `SettingsStore.Write`'s own composed string naming the file, the same one
`StartupFailure.Describe` already surfaces. `Commit()` deliberately no longer rethrows: `FlushAsync()`
has exactly one caller (`PresetsViewModel`), so a rethrow would report a *different* tab's flushed
failure through whichever Presets command happened to trigger the flush, attributing the error to the
wrong place.

**Presets reverts what it shows on a failed write; the other five tabs don't.**
`PresetsViewModel`'s four commands route a failed `SavePreset`/`SetActivePreset`/`DeletePreset` through
a shared `ReportSaveFailure` — log, notify, then `Reload()` unconditionally — because its
`PresetEntryViewModel` fields are bound directly to the entry being edited, so a failed `SaveAsync`
would otherwise leave the *displayed* name and prompt as the failed edit while `Store.Current` still
holds the old value. `Reload()` rebuilds from `Store.Current`, which a failed `Save()` left untouched,
so the tab reverts to what is actually persisted — the fix for "an edited name still shows the edited
text as if it had saved" — while `AddAsync`'s typed-but-unsaved fields survive, since `Reload()` never
touches `NewName`/`NewSystemPrompt`. Audio, General, Hotkey, Logging and Providers write through
`SettingsEditor.Commit()` instead and get only the notification: nothing reverts their bound fields to
the persisted value, because `SettingsEditor` has no per-field view state to refresh the way Presets
does. Neither path retries — the failures issue #37 names, disk-full and permission-denied, are not
transient the way `GeminiProvider`'s three-attempt retry assumes network errors are.

**Three recorder rules are the view model's, not `CaptureAsync`'s.** A capture with no modifier is
refused and recording continues; a captured **bare Escape** is the cancel — read from the capture
rather than from a key event, because both the hook and the focused window see the keystroke in no
guaranteed order, so `Ctrl+Escape` stays bindable; and Change is disabled with a banner whenever
`Availability != Available`, because with `Failed` the returned task never completes and the UI would
sit on "Press a key combination..." for ever. The capture is cancelled on Cancel, on the window
hiding **and on the window being deactivated** — an open capture suspends matching process-wide, so a
user who clicks Change and wanders off has silently disabled their hotkey. `Deactivated` is declared
on `WindowBase`, not on `Window`.

**Hide-on-close is conditional on `WindowCloseReason`.** Only `WindowClosing` is cancelled and turned
into `Hide()`; `ApplicationShutdown` and `OSShutdown` are let through, or Quit could not close an open
window and the process would hang on it. `MainWindow` is deliberately never assigned — harmless today
only because `ShutdownMode.OnExplicitShutdown` is holding it up, and it would couple the
application's lifetime to this window the day someone changes that.

**`[AvaloniaFact]` tests serialize on one dispatcher** regardless of the suite's parallel-by-class, so
`tests/Pisum.Whisper.App.Tests` is slower per test than the other two and the parallelism notes above
do not apply to it. Isolation stays at the default `PerTest`: `PerAssembly` documents itself as unsafe
for tests touching global state, which a settings window backed by a file-writing singleton is.
**`PerTest` is not sufficient either, and it is worth knowing why.** It resets the dispatcher before
every test, but it cannot reset what Avalonia pays once per *process*: the first `ToastWindow` of a
run costs about 135 ms where the second costs 0.8 ms. So a test that reads a value a timer can change
— `ToastPresenter.LiveCount` after a `Present`, with a dwell injected at 30 ms — passes or fails
according to whether a neighbour already paid that cost, which is exactly how four
`ToastPresenterTests` sat green in the suite while failing every time they were run alone. The
lesson generalises past those four: in this assembly, **run a new `[AvaloniaFact]` on its own before
believing it**, and never write one whose assertion races a wall-clock timer. `ready-the-suite-for-ci`
fixed those by giving the tests a dwell they cannot reach rather than by changing the isolation mode.
The
assembly declares `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`, and `TestAppBuilder`
builds a bare `Application` with only `FluentTheme` — it cannot point at `App`, whose constructor takes
an `IServiceProvider` and whose initialisation resolves an orchestrator and registers a tray icon.
`WindowInternals` reaches `Window.HandleClosing` and `WindowBase.HandleDeactivated` by reflection,
because those are the callbacks the platform makes and there is no public route to either.

**Every save logs a hotkey rebind that did not happen.** `HotkeyMatcher.Rebind` early-returns when the
chord is unchanged, but `GlobalHotkeyService.OnSettingsChanged` logs `"Hotkey rebound to {Binding}."`
at `Information` *outside* that `if`. Changing the audio format therefore writes a misleading line at
the default level. Pre-existing, unobservable until this window became the first caller of `Save`, and
deliberately **not** fixed here — the fix belongs to `global-hotkey`.

## Notifications and autostart

`Core/Notifications/` is the policy and `App/Notifications/` is the transport, registered separately
in the shape of `AddTextOutput` plus `AddNativeOutput`: `AddNotifications()` gives
`INotificationService`, and `Program.cs` registers `INotificationPresenter` -> `ToastPresenter`
inline. `INotificationService` has two named methods rather than one with a `force` flag — `Notify`
is a failure and ignores the preference, `NotifyInformation` is chatter and respects
`showTrayNotifications` — because someone who silences status messages still has to be told their API
key is rejected. The preference is read from `SettingsStore.Current` **per call**, matching
`GeminiProviderPool` and `DictationOrchestrator`; nothing subscribes to `Changed` and there is
nothing to rebuild.

**The notification is a window this application draws, and every alternative was rejected on a
structural cost.** `CommunityToolkit.WinUI.Notifications` ships its desktop half only under
`lib/net5.0-windows10.0.18362`, so a plain `net10.0` project resolves `lib/net5.0` and can compose a
toast it can never show — adopting it means a `-windows` TFM, against "one `net10.0` target for every
project", plus an AUMID from a Start-menu shortcut that does not exist until change 12.
`Shell_NotifyIcon` needs no TFM change but does need a *second* notification icon beside Avalonia's,
whose own is unreachable (`Avalonia.Win32.TrayIconImpl` is internal) — and spike S6 found it reaches
no notification platform at all, so it does not even buy the Action Center persistence that was its
one advantage. `osascript -e 'display notification'` attributes every notification to Script Editor,
is a second implementation where drawing one is none, and is a `Process.Start` on the thread that
owns the user's hotkey. Drawing it costs no package, no TFM, no AUMID and no dependency on change 12
on either platform, and it is the only option a headless test can assert. The price is real and is
paid rather than hidden: it ignores Do Not Disturb and Focus Assist, it leaves no history, and the
appearance is ours to own.

**`INotificationPresenter.Present` must not block, and that is a correctness rule.** Two of the six
call sites — "Transcription In Progress" and the capture failure, both in `TryStartRecording` — run
on `GlobalHotkeyService`'s dispatch loop, where the very next item may be the release edge that ends
a hold-to-record dictation. `ToastPresenter.Present` therefore posts to `Dispatcher.UIThread` and
returns; an implementation that waits for the window is wrong. This is the same constraint that keeps
`TextOutput` out of a hook handler, one layer further out, and it is what disqualified `osascript`.
It is invisible at the call site, which is why `DictationNotificationHotkeyCostTests` puts a
one-second presenter behind a real hook and measures that the hook thread is not held.

The toast is `ShowActivated = false` above all else — this application pastes at the cursor in
whatever the user is typing in, so a notification that activates takes away the target the next
dictation would be delivered to. `WindowDecorations` is the 12.1 name; `SystemDecorations` is
obsolete and therefore a build failure. Placement reads `Screens.Primary`'s `WorkingArea` **and**
`Scaling`, because `Position` is physical pixels while `Width` and `Height` are not, and the corner
differs by platform on purpose: bottom-right stacking upward on Windows, top-right stacking downward
on macOS. Three at once at most, a fourth closes the oldest, six seconds each with the dwell injected
so no test waits it out, and no click handling — clicking a non-key window on macOS would make it key
and activate an accessory application, spending exactly the focus the first sentence protects.
`App.OnExit` closes what is still open, so a toast cannot outlive the dispatcher that owns it.

**Autostart is reconciled, not toggled, and it reads before it writes.** `AutostartReconciler` is an
`IHostedService` that compares `IAutostartService.IsEnabled()` with `Current.StartWithSystem` in
`StartAsync` and on every `SettingsStore.Changed`, and writes only on a mismatch. Driving it from
`GeneralViewModel`'s switch instead is the same amount of code and covers less: reconciling also
covers the first launch, a settings file edited by hand and a registration some other tool removed,
through one path with one test. Reading first is the fix for a mistake already in the tree —
`GlobalHotkeyService.OnSettingsChanged` logs a rebind outside `HotkeyMatcher.Rebind`'s early return,
and here the same shape would be a registry mutation on every save rather than a no-op. An
`AutostartException` is caught and logged: a machine that refuses a `Run` value must not cost the
user their dictation hotkey.

Both native locations are injected, defaulting to the real ones, and that is what makes both halves
testable — a private `HKCU` subkey on Windows, a temp directory on macOS. The macOS half writes only
a plist and calls no `launchctl` (`launchd` reads `~/Library/LaunchAgents` at login) and no
`SMAppService` (macOS 13 and a bundle change 12 has not built), so `MacOsAutostartTests` runs from
Windows and only the effect on logging in needs hardware. **`Microsoft.Win32.Registry` needs no
package reference** — the types resolve from the shared framework on `net10.0`, and the only
diagnostics are `CA1416`, cleared by `[SupportedOSPlatform("windows")]` plus the
`OperatingSystem.IsWindows()` guard in `AddNativeAutostart`, exactly as `WindowsClipboard` already
does. Do not add anything to `Directory.Packages.props` for it.

**The transport cannot report a failure raised before the dispatcher loop starts**, and nothing in
*this* capability closes that. A corrupt settings file (issue #20) and a `ValidateOnBuild` failure
both happen in `Main` before `StartWithClassicDesktopLifetime`, so a posted job is enqueued into a
loop that is never started; a missing tray asset throws out of the `App` constructor. That limit is
real and is still the reason `INotificationPresenter` is the transport for none of them — what has
changed is that the off-sequence `report-startup-failures` now covers them, through the native dialog
described in *Startup diagnostics* below. `HotkeyAvailability.Failed`, deferred here by change 9 and
again by change 11, is covered there too, as a notification raised from `App` rather than from where
it is discovered.

## Startup diagnostics

`Core/Diagnostics/` is the vocabulary and `Platform/Diagnostics/` is the transport, in the shape of
`AddTextOutput` plus `AddNativeOutput` — except that neither half is registered, which is the point.
`Program.Main` guards the whole of startup with one `try`/`catch`, and `App.ReportStartupConditions`
reports the two conditions that leave the application running but degraded.

**There are two transports, and they are split by *when* a failure happens rather than by how bad it
is.** Severity is the right answer for the wrong reason: what decides which transport is *reachable*
is whether a dispatcher is pumping.

|              | no dispatcher yet | dispatcher pumping |
|--------------|-------------------|--------------------|
| **fatal**    | `ValidateOnBuild`, an unreadable or unwritable settings file, a missing tray asset → native modal dialog | (empty) |
| **degraded** | an unusable log directory, a hotkey never granted → deferred to `App` | a hotkey revoked while running → toast |

The empty cell is why severity works as a proxy at all: nothing fatal happens after startup, because
everything after startup is a dictation and `DictationOrchestrator` already catches it. The two
degraded conditions in the bottom-left cell are **not** reported where they are discovered — they are
reported from `App.OnFrameworkInitializationCompleted` beside `ShowFirstLaunch`, which is the
precedent and the same argument. Nothing is buffered, because nothing needs to be: `host.Start()` has
returned by then so `Availability` is settled, and `LogDirectory.FailureReason` retains the reason
`TryCreate` used to discard. Both are forced (`Notify`, not `NotifyInformation`) — a hotkey that does
not work makes the application inert, and a log that is not being written removes the only place the
user could have found that out. `App` subscribes to `IGlobalHotkeyService.AvailabilityChanged` **and
then** reads `Availability`, keeping the last reported value: reading first would lose a transition
landing between the two, which is the tray icon's seeded `ApplyState` reasoning.

**`IFatalErrorReporter` is constructed, never registered, and it must stay that way.** One of its
four call sites is `builder.Build()` failing, so a reporter resolved from the container is a reporter
that does not exist exactly when it is needed. `NativeFatalErrorReporter.Create()` returns
`MessageBoxW` on Windows, `osascript -e 'display dialog'` on macOS, and — diverging from
`AddNativeOutput` and `AddNativeAutostart`, which throw `PlatformNotSupportedException` — a **no-op**
on anything else, because this is the thing that reports startup failing and must not be a startup
failure itself. `NativeFatalErrorReporterRegistrationTests` asserts the omission, so a later change
that "fixes" it fails a test that says why. `Report` swallows everything for the same reason: losing
the dialog is bad, losing the exit code and the `Fatal` line behind it is worse.

**`Program` owns the Serilog logger now, and restoring `dispose: true` silently breaks the fatal
path.** `AddFileLogging` registers it with `services.AddSerilog(serilog)` — whose `dispose`
parameter defaults to `false`, which is the whole point — and hands it back
through an `out` parameter that `BuildHost` assigns *immediately* after that call, before anything
that can throw — which is what lets one catch in `Main` write a `Fatal` line for a container that
never finished building. Put `dispose: true` back and the logger has two owners; the container
disposes it inside the `try`, and the fatal path stops writing anything with no test failing. That is
what `FileLoggingRegistrationTests.DisposingTheLogger_DrainsTheQueueRatherThanDiscardingIt` and
`TheContainerDoesNotDisposeTheLogger` are for, and it is why `FileLoggingTestBase` now disposes the
loggers its hosts were built with. In the catch the logger is disposed **before** the dialog, because
the asynchronous sink drops its queue rather than draining it and `MessageBoxW` blocks until someone
dismisses it — at login, possibly never.

**`using var host` must stay out of the `try`.** A `using var` releases at the end of its own block,
*before* the matching `catch` runs, so putting it inside would hand the catch an already-disposed
host. `host` is a local disposed in the `finally` instead. `BuildHost` has no catch of its own on
purpose: with the `out` assignment above, `Main`'s catch already logs, disposes and reports for all
four fatal cases, and keeping both would write two `Fatal` lines and dispose the logger twice. It
returns 1 rather than rethrowing, or the operating system's own crash dialog would land on top of
ours.

**Nothing may call `Report` from an automated test.** `MessageBoxW` pumps its own modal loop and
blocks until it is dismissed, so a test that showed the dialog would hang the run with no timeout —
the run would be waiting on a person. What is testable is which type `Create()` returns and the
AppleScript escaping; the dialogs themselves are verified by hand, and **the macOS pass has not been
run** (the change's task 7.2, which belongs to the sitting changes 8 to 11 already owe).
`AppleScript.Escape` is deliberately **not** on `MacOsFatalErrorReporter`: that type carries
`[SupportedOSPlatform("macos")]`, and calling a macOS-only member from an unguarded test is a CA1416
error — which would put the one part of the macOS half Windows can verify out of reach of the tests
that justify it.

**`AppleScript.Escape` must escape line breaks, not only quotes and backslashes.** An AppleScript
string literal cannot span lines and the whole script is one `-e` argument, so a raw line break is a
syntax error, `osascript` draws nothing, and `Report` swallows the non-zero exit — the silent total
failure the escaping exists to prevent. This is not a hypothetical input:
`StartupFailure.Describe` ends *every* message with a blank line and the log path, so dropping the
newline passes breaks the macOS dialog for **all** four fatal cases at once, and does it on the one
platform whose verification is by hand. The order is load-bearing — backslashes first, so the
backslash each `\n` introduces is AppleScript's own escape rather than a doubled literal, and CRLF
before the single characters so a Windows line ending is not two blank lines.
`AppleScriptEscapingTests` covers all of it.

**A parse failure's message is passed through unchanged**, and that was measured rather than assumed.
`SettingsException` embeds `JsonException.Message` and the settings file holds API keys in plaintext,
so the error templates in `System.Text.Json.dll` at 10.0.8 were read: every reader template formats
one *offending character*, and `JsonException.Path` names the failing property and never its value.
Summarising to a line and a position would delete exactly what issue #20 holds up as the actionable
part. `StartupFailureTests.AParseFailureInsideAnApiKeyDisclosesNoPartOfIt` guards it against a
genuine parse failure over a document holding a key.

## Spec-driven workflow (OpenSpec)

`openspec/config.yaml` sets `schema: spec-driven`. Change proposals live in `openspec/changes/`,
completed ones move to `openspec/changes/archive/`, and capability specs land in `openspec/specs/`.
`openspec/ROADMAP.md` sequences the work as **12 ordered changes**, each tracked by a GitHub issue
labelled `change:NN`. **Changes 1 through 11 are archived** and their `application-host`,
`settings-persistence`, `file-logging`, `audio-capture`, `audio-encoding`, `global-hotkey`,
`gemini-transcription`, `text-output`, `dictation-pipeline`, `tray-icon`, `settings-window`,
`notifications` and `autostart` specs are synced, so read every one of them from `openspec/specs/`
like any other. Only change 12 (`add-packaging-ci` — `packaging`) is still active, and it is a lone
`proposal.md` — no design, no tasks and no delta specs — so it is not archivable as it stands.
`migrate-tests-to-xunit-v3`, `report-startup-failures`, `fix-startup-ioexception-mislabeling` and
`surface-settings-save-failures` are archived as well; they carry no number, by the roadmap's own rule
that off-sequence work gets a section instead of one. The second added `startup-diagnostics` and
extended `file-logging` and `global-hotkey`, and all three are synced. The third closed issue #34,
which `report-startup-failures`'s own archived `design.md` had already reproduced and deliberately
deferred: `SettingsStore.Read` and `Write` now both wrap `IOException` and `UnauthorizedAccessException`
into `SettingsException`, so `StartupFailure.Describe` no longer carries a type-matching arm that
mislabels an unrelated I/O failure (a missing tray asset's `FileNotFoundException`, issue #34's
reproduction) as a settings error. It also added a `settings-persistence` requirement that a write
failure is reported the same way on first launch and on a later save; both deltas —
`startup-diagnostics` and `settings-persistence` — are synced. The fourth closed issue #37 — a settings
write that never reaches disk was an unobserved task exception, not a report — and its
`settings-window` delta is the one `SettingsEditor.Commit()` and `PresetsViewModel`'s notify-and-reload
handling below now implement; it too is synced.

**Seven archived changes carry unchecked manual-verification tasks, ten in total** — 1's 1.5a, 7's
3.3, 8's 6.3 and 6.4, 10's 6.4, 11's 7.1 and 7.4, `migrate-tests-to-xunit-v3`'s 5.4, and
`report-startup-failures`'s 7.2 and 7.3. An earlier version of this sentence named only 8, 10, 11
and `report-startup-failures`; `ROADMAP.md` corrected the same error in its *Artifact status*
section and this is the same correction — changes 1, 7 and the xUnit migration are in exactly the
same state, so there was never a departure to explain, only three rows left out of the count.

`report-startup-failures`'s 7.1 closed 2026-09-02, all four fatal-dialog reproductions run — the
corrupt-settings one closed issue #20, the other three ran later that day under
`settle-win-x64-verification-debt` — and 7.3's interactive-launch half passed alongside them, leaving
only its login-time half. Every one of these open items still needs a person at a machine, and the
macOS ones need Apple Silicon with Accessibility granted.

**All ten are currently tracked by nothing.** Issues #30 (win-x64) and #31 (Apple Silicon) were both
closed on 2026-09-02 with work still open — #30 while six of its eleven checks had not run, #31 with
change 8's refused-microphone case abandoned rather than completed. `ROADMAP.md`'s *Artifact status*
table is therefore the only surviving record, and that is exactly the state the standing decision
about moving open by-hand tasks to a tracking issue exists to prevent. Verified 2026-09-03: both
report `CLOSED`.

An archived change here therefore does **not** certify that its capability was verified on hardware:
what each still owes, and the open design questions those runs would settle, stay in that change's
archived `design.md` and are reflected nowhere under `openspec/specs/`. Read the archived `tasks.md`
before treating a capability spec as verified behaviour rather than intended behaviour. The macOS
verification change 1 left unfinished was tracked by issue #15 rather than by an open change, and
closed on 2026-08-28; issue #31 carried the same shape forward for changes 1, 7, 8, 9, 10 and 11,
and ran everything but 8's refused-microphone case on 2026-09-02 — then closed anyway, unlike #15,
which had nothing left. Change 9's manual verification is entirely closed as a result, which is why
it is absent from the list this paragraph opens with. Drive the workflow with the `/opsx:*` commands
(`explore`, `propose`, `apply`, `sync`, `archive`); the backing skills are in
`.claude/skills/openspec-*`. The bottom of `openspec/config.yaml` carries the project context and
the per-artifact rules, and both are **live, not the commented-out template**: `proposal`, `design`
and `tasks` each have rules, `specs` deliberately has none, and the `openspec` CLI is not installed
on this machine — so `/opsx:archive` and `/opsx:sync` are run by reading the change folder directly,
and a spec sync is the no-rules case either way.

**A commit that only touches `openspec/` must say so in its subject line.** A proposal's *What
Changes* section is a list of imperatives — "Replace `MSTest` with `xunit.v3`", "Rewrite the
attributes across 53 classes", "Update `README.md`" — which is indistinguishable in shape from a
changelog of work already done. Summarise that diff and you get a commit message describing a
migration that has not started, and a generated message will do exactly that: it happened twice on
the `xunit` branch, once claiming "372 tests green, 0 warnings" on a commit containing four markdown
files. This is not a tool defect to wait out, because the ambiguity is in the artifact itself and
every planning commit for changes 9 through 12 will hit it. So lead with a verb that names the act
of planning — `Plan the …`, `Record …`, `Repin …` — and open the body with what has **not** been
done yet. Verify the message against `git show --stat`, not against the branch name.

## Code Intelligence

For any question about code inside this repo — how something works,
how X reaches Y, blast radius of a change, where a symbol is used —
call `codegraph_explore` first. Don't grep or re-read files for
these questions; the tool returns verbatim source, call paths
(including dynamic-dispatch hops), and blast radius in one call.

The server is registered in `mcp.json` at the repo root (`codegraph serve --mcp`); its index lives
in `.codegraph/`, which is git-ignored apart from that ignore file.

## Tool Preference: JetBrains MCP over built-in tools

When a JetBrains IDE MCP server is connected, ALWAYS prefer its tools over
built-in file/search tools for anything touching source files in this repo:

- Finding files → use `find_files_by_name_substring` / `search_in_files_by_text`
  instead of `Grep`/`Glob`/`find`/`grep` via Bash
- Reading file content → use `get_file_text_by_path` instead of `Read`
- Editing/replacing text → use `replace_text_in_file` / `replace_specific_text`
  instead of the built-in `Edit` tool or `sed`/`awk` via Bash
- Creating files → use `create_new_file_with_text` instead of `Write`

Rationale: the JetBrains tools operate on the IDE's live index (respects
.gitignore, refactoring-safe, triggers IDE-side formatting/inspections),
so results and edits stay consistent with what's open in Rider. Only fall
back to built-in tools if the MCP connection is unavailable or a JetBrains
tool call fails.
