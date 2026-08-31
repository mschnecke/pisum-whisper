# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Layout

```
Pisum.Whisper.slnx
├── src/Pisum.Whisper.Core        domain + orchestration; no platform or UI dependencies
├── src/Pisum.Whisper.Platform    the OS-specific surface (the clipboard and paste probes today)
├── src/Pisum.Whisper.App         Avalonia tray shell and the composition root
├── tests/Pisum.Whisper.Core.Tests
└── tests/Pisum.Whisper.Platform.Tests   native registration, and the manual clipboard round trip

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
isolate that class. The one that does is `FileLoggingRotationTests`, which asserts a p99.9 write
latency under 500 µs and therefore measures the machine as much as the code — it sits in a
`DisableParallelization` collection and is **still occasionally over the bound**. A lone failure
there is a busy machine, not a regression in the logging path; two in a row is worth looking at. The
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
real `HttpClient` over a fake handler and never reach the network. The split is 23 / 26 / 4 classes and
189 / 179 / 4 tests — they sum to 372, so exactly one category applies to every test.

```bash
dotnet test Pisum.Whisper.slnx --filter-trait Category=Unit          # 189, no I/O at all
dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual    # 368, what CI should run
```

Keep the rule mechanical when adding a class: if its constructor or its base's reaches
`Path.GetTempPath`, `Directory.CreateDirectory`, `File.WriteAll*`, `new ServiceCollection` or
`Host.CreateApplicationBuilder`, it is `Integration`. `TextOutputTestBase` is the one base that
is not — it builds a fake clipboard, a fake probe and a `TestProvider`, all in memory — so its four
derived classes are `Unit`.

**`[TestCleanup]` is `Dispose()` now.** MSTest and xUnit agree on a fresh instance per test method, so
a lifecycle pair is a constructor and `IDisposable` — there are fourteen, and four of them are on
abstract bases whose derived classes add nothing. Assertions are Shouldly throughout; `xunit.v3.assert`
comes along with the framework but is unused.

## Spikes

`spikes/Pisum.Whisper.Spikes` is deliberately outside the solution. It was kept until the macOS
verification tracked by issue #15 was done, so that the harness could be **re-run rather than
re-written**; #15 closed on 2026-08-28, which by the rule written here makes the harness deletable.
It is still on disk — deleting it is a decision for whoever no longer wants the two macOS `FAIL`
rows re-runnable, not an oversight.

```bash
dotnet run --project spikes/Pisum.Whisper.Spikes -- hook       # global hook, both key edges
dotnet run --project spikes/Pisum.Whisper.Spikes -- paste      # simulated Ctrl+V into Notepad
dotnet run --project spikes/Pisum.Whisper.Spikes -- audio      # capture format and rate conversion
dotnet run --project spikes/Pisum.Whisper.Spikes -- opus       # Ogg/Opus encode + decode round trip
dotnet run --project spikes/Pisum.Whisper.Spikes -- tray       # tray icon, tooltip, runtime swap
dotnet run --project spikes/Pisum.Whisper.Spikes -- combined   # hook + Avalonia run loop together
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

Five rules every later change is written against:

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

## Spec-driven workflow (OpenSpec)

`openspec/config.yaml` sets `schema: spec-driven`. Change proposals live in `openspec/changes/`,
completed ones move to `openspec/changes/archive/`, and capability specs land in `openspec/specs/`.
`openspec/ROADMAP.md` sequences the work as **12 ordered changes**, each tracked by a GitHub issue
labelled `change:NN`. Changes 1 through 7 are archived and their `application-host`,
`settings-persistence`, `file-logging`, `audio-capture`, `audio-encoding`, `global-hotkey`,
`gemini-transcription` and `text-output` specs are synced, so read them from `openspec/specs/` like
any other; the macOS verification change 1 left unfinished was tracked by issue #15 rather than by
an open change, and closed on 2026-08-28. Drive
the workflow with the `/opsx:*` commands (`explore`, `propose`, `apply`, `sync`, `archive`); the
backing skills are in `.claude/skills/openspec-*`. Project context and per-artifact rules can be
filled in at the bottom of `openspec/config.yaml` (all commented out today).

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
