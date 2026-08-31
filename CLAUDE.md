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
