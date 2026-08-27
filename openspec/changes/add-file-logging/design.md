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

**Both size-based rolling and age-based deletion.** These are not redundant. Rolling by size caps a
single busy session but never removes a small file from six months ago; age-based deletion removes
stale files but does nothing about one runaway session. The reference implements both and this
follows it.
*Alternative rejected:* daily rolling with a retained-file count. It expresses "keep N days" more
directly but does not bound a single day's file, and a debug-level session can produce a very large
one.

**`LoggingLevelSwitch` held by the logging component and mutated on settings change.** This is the
direct analogue of the reference's reloadable `EnvFilter`, and it is the whole point of the feature:
a level that requires a restart to change is useless for capturing an intermittent problem, because
restarting destroys the state that caused it.

**Age sweep runs once at startup, not on a timer.** The reference does the same. A background timer
adds a lifecycle to manage for a housekeeping task with no deadline; a desktop utility restarts often
enough.

**Logging initialises after settings load, not before.** It needs `LoggingConfig` for the path, size
and retention. Anything that must be reported before that point goes to the console under `#if DEBUG`.
This creates a genuine ordering constraint on the composition root, and a small window at startup
where failures are not logged to file — accepted, because the alternative is a second bootstrap
configuration that would then have to be reconciled with the real one.

**Failure to set up file logging must not prevent startup.** A locked or unwritable log directory is
an inconvenience; refusing to launch a dictation tool over it is not proportionate.

**Console sink only under `#if DEBUG`.** The release build is a windowless tray process with nowhere
for console output to go.

## Risks / Trade-offs

- **The startup window before logging exists is unlogged.** → Small and bounded: only settings
  resolution happens first. Accepted rather than adding a second logger configuration.

- **Serilog's own failures are silent by design.** → Enable `SelfLog` to the debug console in debug
  builds so a misconfigured sink is visible during development.

- **Logs may contain sensitive text.** Transcripts are user speech and settings contain API keys. →
  Never log transcript content or API key values; log lengths, categories and outcomes instead. This
  needs stating now, because every later change writes log statements against this decision.

- **Runtime level changes could be missed if a component caches a logger's enabled state.** → The
  level switch is evaluated per write by Serilog, so this holds as long as nothing caches
  `IsEnabled`. Worth a test.

## Open Questions

None. The size, retention and level defaults are inherited from the reference (1 MB, 7 days, `info`)
and are exposed as settings, so no value needs to be guessed here.
