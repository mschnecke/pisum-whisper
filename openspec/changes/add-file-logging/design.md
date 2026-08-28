## Context

This application fails where the user cannot see it. It has no window most of the time, its work
happens on background threads, and its failure modes — a hook that never fires, a microphone that
returns silence, a Gemini call that returns 429 — all look identical from outside: nothing happens.
Without a log on disk, every bug report is "it didn't work".

The reference added file logging as a dedicated PRD late in its life
(`W:\github-pisum-transcript\docs\PRD-file-logging.md`), which is a reasonable signal that it was
needed in practice rather than anticipated.

## Goals / Non-Goals

**Goals:**
- Durable diagnostics on disk, bounded in both size and age.
- Runtime verbosity change, so a user can reproduce a problem at `debug` without restarting.
- A logging abstraction registered early enough that every later change uses it natively.

**Non-Goals:**
- The Logging settings tab, which ships with `add-settings-window`.
- The "Open Log Folder" button; this change exposes the path, the button is UI.
- Remote log shipping, crash reporting or telemetry.

## Decisions

**Serilog with `Serilog.Sinks.File`, behind `Microsoft.Extensions.Logging`.** Consumers depend on
`ILogger<T>`, not on Serilog, so the sink is replaceable. Serilog is chosen for one specific reason:
`LoggingLevelSwitch` gives runtime level changes, which is a hard requirement here and awkward with
the built-in providers.

**The subsystem lives in `Core/Logging/`, and its registration is an `AddFileLogging` extension on
`IServiceCollection` that `Program.cs` calls.** `Pisum.Whisper.Platform` is ruled out by change 1's
own enumeration of its scope — "autostart, notifications, opening a folder, a macOS permission
check" — which contains no logging; note that *opening* the log folder does belong there, in change
10, while resolving its path belongs here. Between `Core` and `App`, `Core` follows the precedent
change 2 set for `SettingsStore`: filesystem infrastructure with an explicit-path constructor
overload, tested against real temporary directories in `Core.Tests`. CLAUDE.md constrains `Core` to
"no platform or UI dependencies", and Serilog is neither. Keeping the *registration* in `Core` as
well is what makes the regression test for the level switch meaningful: if the wiring sat in
`Program.cs`, a test could only reconstruct it and would sail past a regression introduced in
`Program.cs` itself. `Program.cs` holds a call, so there is little there to drift. Package split
follows: `Core` takes `Serilog`, `Serilog.Sinks.File`, `Serilog.Sinks.Async` and
`Serilog.Extensions.Hosting`; `App` takes nothing new.
*Alternative rejected:* the wiring in `App` with a new `tests/Pisum.Whisper.App.Tests` project. It
is the cleaner architectural story — composition confined to the composition root — but costs a
project and a `.slnx` entry that this change does not otherwise need. Worth revisiting if the wiring
ever acquires Avalonia-specific concerns, or once changes 9 to 11 need App test infrastructure
anyway; adding it now for that reason would be speculative.
*The cost of this placement:* Serilog becomes reachable from `Platform` and `App` transitively, so
the static `Serilog.Log` is in scope project-wide. It is not used anywhere — the configured logger is
always passed explicitly — which the decision on the peek below already requires for its own reasons.
That is a convention to state rather than a boundary to enforce, and it does not weaken the first
decision above: consumers still take `ILogger<T>` and the sink stays replaceable.

**Registered through `Serilog.Extensions.Hosting`'s `AddSerilog`, which replaces `ILoggerFactory`
outright, and passing `dispose: true`.** The host's default providers — Console, Debug, and on
Windows an Event Log writer — must not survive alongside Serilog. `ILogger<T>.IsEnabled` answers "is
*any* provider enabled", so a surviving console provider reports `true` while Serilog's switch drops
the event: an `if (_logger.IsEnabled(Debug))` guard then does its expensive work for nothing, in
exactly the audio callbacks that change 4 puts at trace level. Replacing the factory makes their
absence structural. `dispose: true` is not optional once the sink is asynchronous: the parameter
defaults to `false`, and measured against a clean host disposal that default discards the queue
rather than draining it — a run of 500 events produced an empty file. It is a one-word omission that
silently empties the log, and it would pass every test written before the async sink existed.
*Alternative rejected:* `Serilog.Extensions.Logging` with `builder.Logging.AddSerilog` plus
`ClearProviders()`. Measured as behaviourally identical, `IsEnabled` included; rejected only because
the guarantee then rests on a call that a later edit can drop without any visible symptom.

**`Microsoft.Extensions.Logging` does not gate Serilog, and must not be made to.** Measured:
`AddSerilog` installs a provider-scoped filter rule at `Trace`, and MEL resolves the most specific
matching rule, so that rule outranks `LoggerFilterOptions.MinLevel` and everything reaches Serilog's
switch in both Debug and Release builds. This is recorded because it is counter-intuitive — MEL's
own default minimum is `Information` — and a reader who checks only that will "fix" a problem that
does not exist by calling `SetMinimumLevel`, putting a second gate in front of the runtime level
change and silently breaking the feature. For the same reason the existing
`#if DEBUG builder.Logging.SetMinimumLevel(LogLevel.Debug)` in `Program.cs` is removed: once Serilog
is wired it no longer influences it, but it reads as though it does.

**Both size-based rolling and age-based deletion.** These are not redundant. Rolling by size caps a
single busy session but never removes a small file from six months ago; age-based deletion removes
stale files but does nothing about one runaway session. The reference implements both and this
follows it.
*Alternative rejected:* daily rolling with a retained-file count. It expresses "keep N days" more
directly but does not bound a single day's file, and a debug-level session can produce a very large
one.

**`retainedFileCountLimit: 10`, adopting Serilog's counting rather than the reference's.** Two things
were unverified here and both were measured. First, retention does prune sequence-numbered files
under `RollingInterval.Infinite`: 4,000 events at a 1 KB limit produced 445 files unbounded and
exactly 10 with the limit set, so no additional pruning of our own is needed. Second, the limit
counts the *active* file within it — ten on disk means nine rolled plus the one being written —
whereas the reference's appender is documented as keeping ten *rotated* files plus the active one.
That is a one-file difference, immaterial to the bound's purpose at a 1 MB default, and not worth
carrying a `11` in the configuration that every future reader would have to have explained. The spec
is worded to Serilog's semantics instead, so the test cannot encode the wrong assumption.
Worth knowing when reading a log directory: Serilog rolls to `pisum-whisper_001.log`,
`pisum-whisper_002.log` and so on, and the *highest* sequence number is the live file. The
base-named `pisum-whisper.log` is the oldest, not the newest, and is pruned like any other. The age
sweep therefore matches `pisum-whisper*.log`, with the sequence before the extension rather than
after it as in the reference.

**The file sink is wrapped in `Serilog.Sinks.Async`, and the inner file sink is not buffered.** The
justification is the roll, not throughput. Measured per-call latency on the calling thread over
30,000 events with rolling enabled: synchronous writes sit at 15.5 µs (p50) and 97 µs (p99), both
comfortable inside a capture callback's budget, but reach **1.74 ms at p99.9** — that is the roll
itself, closing the file, opening the next, enumerating the directory and applying retention.
Wrapping in `Async` moves it off the calling thread: 1.7 µs (p50), 3.5 µs (p99), **34 µs (p99.9)**, a
fiftyfold improvement at exactly the tail that drops audio frames. General throughput was never the
problem, and saying so keeps the justification honest and narrow.
*Alternative rejected:* `buffered: true` on the file sink. It is the obvious cheap fix and it does
help the synchronous case (p50 17.5 µs → 6.2 µs), but it keeps the work on the calling thread, and
once `Async` is in place it makes the tail *worse* (p99.9 34 µs → 64 µs) while widening the window of
events lost when the process dies — and in a diagnostics feature the events just before a crash are
the ones worth having.

**`blockWhenFull` stays at its default `false`, and dropped events are monitored rather than
assumed away.** `true` looks like the safe choice and is the opposite of it. Measured, driving 3,000
events into a 100-slot buffer against a sink draining at 1 ms per event: `false` held the calling
thread for 30 ms and dropped 2,899 events; `true` lost nothing and held the calling thread for
**45 seconds**. In a capture callback that is not slow, it is total capture failure. Log backpressure
must never become audio backpressure, so dropping log lines is the correct trade. That makes the loss
silent, which is why an `IAsyncLogEventSinkMonitor` is attached and `DroppedMessagesCount` is logged
at shutdown. In practice the buffer should never fill — the file sink drains on the order of 60,000
events per second against an audio path producing hundreds — but "should never" is not a claim a
diagnostics subsystem gets to make without an assertion.

**Trace-level statements in the audio path carry no `IsEnabled` guard.** Measured, a suppressed call
with the switch at `information` costs about 0.1 µs, so the guard would cost more than it saves.
Recorded because it closes from the other side the same concern that motivated the provider decision
above: the guards are unnecessary, *and* they would have been unreliable had a stray provider
survived to answer `IsEnabled` on its own level.

**`LoggingLevelSwitch` held by the logging component and mutated on settings change.** This is the
direct analogue of the reference's reloadable `EnvFilter`, and it is the whole point of the feature:
a level that requires a restart to change is useless for capturing an intermittent problem, because
restarting destroys the state that caused it. The subscriber is an `IHostedService` registered by
`AddFileLogging`, so `host.Start()` is what guarantees it exists — a singleton nobody resolves never
subscribes, and `ValidateOnBuild` validates registrations without instantiating them. The initial
level comes from `SettingsStore.Current`, not from the event, because `Changed` is raised only from
`Save`.

**Five level names — `trace`, `debug`, `info`, `warn`, `error` — matched case-insensitively.** This
is the reference's `EnvFilter` vocabulary and the PRD's five levels, and it is what `LoggingConfig`
already documents; change 2 settled it and it does not get to drift here. They map to Serilog's
`Verbose`, `Debug`, `Information`, `Warning` and `Error`. A scenario in this change's own spec read
`warning`, which was a slip in the spec rather than a second vocabulary, and the spec is corrected.
Matching is case-insensitive because `tracing`'s `EnvFilter` is and the file is hand-editable — that
is parity, not added flexibility. Serilog's own spellings are deliberately *not* accepted as
aliases: the settings window in change 10 presents a dropdown, so free text only reaches this from a
hand-edit, and an unrecognised value already falls back to `info` with a warning that names what was
found. `LoggingConfig`'s doc comment, which today trails off into "or other custom-defined levels",
is corrected to name exactly these five, since this change is what makes the property mean anything.

**Logging is configured from a read-only peek at `LoggingConfig`, before the container is built.**
Only `logMaxFileSizeMb` genuinely forces logging to wait for settings — the log path is fixed, the
level is what the switch exists for, and `logRetentionDays` is consumed by the sweep rather than the
sink. That is too thin a reason to start the process unlogged, so a small read-only helper
deserialises `~/.pisum-whisper.json` with the existing `SettingsJsonContext`, takes `LoggingConfig`,
and returns defaults on a missing, unreadable or unparseable file. It never writes and never throws,
so it cannot itself need logging. `SettingsStore.Load()` then stays exactly where change 2 put it,
runs with a real logger, and its repair warnings are captured.
*Alternative rejected:* accepting an unlogged startup window, which the earlier draft of this design
did. The cost is worse than it looks: the dangling-preset repair re-persists the file, so its warning
fires once and never again — losing it loses it permanently, for precisely the "why did my preset
change?" report this change exists to serve.
*Alternative rejected:* Serilog's `CreateBootstrapLogger` with a later `Reload`. Measured to work,
including reloading onto the same file path without a lock failure, and an `ILogger<T>` handed out
before the reload does route to the new sinks after it. Rejected because it requires the global
mutable `Log.Logger`, adds reload sequencing to get wrong, and leaves the first lines at a default
level rather than the user's — all to avoid one extra read of a file of roughly one kilobyte. Note
it does *not* replay pre-reload events; the window is closed by writing to the real file from the
start, not by buffering.

**Configuring logging before the container is built also means container validation failures are
logged.** `ValidateOnBuild` turns an unsatisfiable registration into a throw from `builder.Build()`,
which the `application-host` spec requires to name the offending service. In a windowless tray
process that diagnostic currently reaches stderr and nowhere else.

**Age sweep runs once at startup, not on a timer.** The reference does the same. A background timer
adds a lifecycle to manage for a housekeeping task with no deadline; a desktop utility restarts often
enough.

**The sweep runs before the file sink is opened.** Serilog opens the log file with `FileShare.Read`,
which excludes delete. If the application has not run for longer than `logRetentionDays`, the stale
`pisum-whisper.log` is both a delete candidate and the file Serilog has just reopened for append, so
a sweep placed after the sink silently fails against it and that file is never cleaned — it is
appended to and grows across every subsequent run. The age bound then stops holding in exactly the
case it was written for. Verified by experiment, not assumed. The order is therefore: resolve the
directory, create it, sweep, build the logger, build the host.

**The sweep reports through the logger it precedes.** Running first means it has no logger, so it
returns what it removed and the caller logs that once the logger exists, rather than staying silent
or acquiring a logger of its own.

**Failure to set up file logging must not prevent startup.** A locked or unwritable log directory is
an inconvenience; refusing to launch a dictation tool over it is not proportionate. The reporting
falls out of attaching the file sink conditionally: build one `LoggerConfiguration`, add the file
sink only when the directory proved usable, always add the debug console sink, create the logger, and
then log the remembered failure through it at `Error`. The application runs with a working logger
that states why it is not writing to a file.

**Console sink only under `#if DEBUG`.** The release build is a windowless tray process with nowhere
for console output to go. This is now a Serilog sink rather than MEL's default console provider,
which the registration decision above removes — so the startup line `CLAUDE.md` documents as the sign
the app came up changes format, and that document is updated with it.

## Risks / Trade-offs

- **Events still queued in the async sink are lost if the process terminates without disposing it.**
  → `dispose: true` covers every clean exit, and `Environment.Exit` and `FailFast` are not used. The
  window is small because the worker drains continuously and the inner file sink is unbuffered, which
  is a second reason not to buffer it.

- **The async sink drops rather than blocks under sustained overload, silently.** → Accepted
  deliberately, because the alternative stalls the audio thread. Mitigated by monitoring
  `DroppedMessagesCount` and logging it at shutdown, so the loss is visible rather than invisible.

- **Draining the queue adds roughly 130 ms to shutdown.** → Invisible in a tray application, and
  within what the `application-host` clean-shutdown requirement allows.

- **The settings file is read twice at startup.** → It is roughly a kilobyte of JSON read once more
  on a path that already touches the disk several times. Accepted as the cost of the peek.

- **Serilog's own failures are silent by design.** → Enable `SelfLog` to the debug console in debug
  builds so a misconfigured sink is visible during development.

- **Logs may contain sensitive text.** Transcripts are user speech and settings contain API keys. →
  Never log transcript content or API key values; log lengths, categories and outcomes instead. This
  needs stating now, because every later change writes log statements against this decision.

- **A runtime level change can be defeated by a second gate in front of the switch.** The earlier
  draft attributed this to components caching `IsEnabled`. Measured, that is not the mechanism:
  Serilog evaluates the switch per write, and `ILogger<T>.IsEnabled` tracks it exactly once the
  factory is replaced. The real mechanism is a stray provider — one left registered by the host, or
  an `ILoggerFactory` that was not replaced — answering `IsEnabled` on its own level. → Covered by
  asserting the resolved `ILoggerFactory` and by testing `IsEnabled` against the switch, not by
  auditing call sites for caching.

## Open Questions

None. The four that this design carried while it was being reviewed against the code changes 1 and 2
landed — non-blocking writes, where the subsystem lives, the retention off-by-one and the level
vocabulary — are all resolved above, the first two by decision and the last two by measurement and by
deferring to what change 2 already settled.
