## 1. Sink setup

- [ ] 1.1 Add `Serilog`, `Serilog.Sinks.File`, `Serilog.Sinks.Async` and `Serilog.Extensions.Hosting` to central package management and reference them from `Pisum.Whisper.Core`; `Pisum.Whisper.App` gains no new package. Verify: `dotnet build Pisum.Whisper.slnx` stays at 0 warnings.
- [ ] 1.2 Add a read-only `LoggingConfig` peek in `Core/Logging/` that deserialises `~/.pisum-whisper.json` through the existing `SettingsJsonContext` and returns defaults for a missing, unreadable or unparseable file. It must never write and never throw. Verify: unit tests over a temp home for the absent, valid, partial and corrupt cases, each asserting no file was created or modified.
- [ ] 1.3 Add `AddFileLogging` as an `IServiceCollection` extension in `Core/Logging/` that builds the Serilog logger from the peeked config and registers it with `AddSerilog(logger, dispose: true)`, and call it from `Program.cs` before the container is built so a container validation failure is logged rather than reaching stderr alone. Verify: run the app and confirm `~/.pisum-whisper/logs/pisum-whisper.log` is created and contains a startup line; temporarily break a registration and confirm the `ValidateOnBuild` failure naming the service appears in the file.
- [ ] 1.4 Remove `#if DEBUG builder.Logging.SetMinimumLevel(LogLevel.Debug)` from `Program.cs`; the level switch's initial value replaces it. Verify: a release build writes `debug` output to the file when `logLevel` is `debug`, and a debug build writes `trace` output when `logLevel` is `trace`.
- [ ] 1.5 Assert the host's default providers do not survive the Serilog registration. Verify: unit test builds a host through `AddFileLogging`, resolves `ILoggerFactory` and asserts it is `SerilogLoggerFactory`, and asserts `ILogger<T>.IsEnabled(Debug)` is false while the switch sits at `information`.
- [ ] 1.6 Resolve and expose the log directory from `Core/Logging/`, creating it if absent, with an explicit-path constructor overload following `SettingsStore`'s pattern. Verify: unit test against a temp home asserts the resolved path and that the directory is created.
- [ ] 1.7 Make setup failure non-fatal by attaching the file sink only when the directory proved usable, and logging the reason at `Error` through the logger that is built regardless. Verify: unit test with an unwritable path asserts no exception escapes, that the resolved logger is still usable, and that the failure was logged.
- [ ] 1.8 Enable Serilog `SelfLog` to the debug console under `#if DEBUG` only. Verify: misconfigure a sink temporarily and confirm the diagnostic appears, then revert.
- [ ] 1.9 Add a Serilog console sink under `#if DEBUG` only, and update the startup signal documented in `CLAUDE.md` to the line a debug build now actually prints. Verify: debug build prints the startup line to the console, release build produces no console output, and the string in `CLAUDE.md` matches the debug build's output verbatim.

## 2. Size and age bounds

- [ ] 2.1 Configure size-based rolling from the peeked `logMaxFileSizeMb` with `retainedFileCountLimit` of 10. Verify: integration test sets a 1 KB limit, writes until it rolls, flushes, and asserts a second file exists.
- [ ] 2.2 Assert the retained-file cap. Verify: integration test rolls well past ten times, flushes, and asserts exactly ten files remain — nine rolled plus the active one, since `retainedFileCountLimit` counts the active file within its limit.
- [ ] 2.3 Implement the age sweep over `pisum-whisper*.log` files older than `logRetentionDays`, returning what it removed rather than logging it. Note Serilog rolls to `pisum-whisper_001.log`, so the sequence precedes the extension and the base-named file is the oldest rather than the newest. Verify: unit test creates files with backdated timestamps either side of the boundary and asserts only the expired ones are deleted.
- [ ] 2.4 Run the sweep before the file sink is opened, and log its result once the logger exists. Verify: integration test backdates the active `pisum-whisper.log` past the retention window, then starts logging, and asserts the stale file was removed rather than appended to — this test fails if the sweep runs after the sink opens, because Serilog holds the file with `FileShare.Read`.

## 3. Non-blocking writes

- [ ] 3.1 Wrap the file sink in `WriteTo.Async`, leaving the inner file sink unbuffered. Verify: measure per-call latency on the calling thread over at least 10,000 events spanning a roll, and confirm p99.9 stays in the tens of microseconds rather than the ~1.7 ms the synchronous sink shows.
- [ ] 3.2 Leave `blockWhenFull` at its default `false`. Verify: unit test drives more events than the buffer holds through a deliberately slow inner sink and asserts the enqueueing thread is not held for the drain time — the same test runs for minutes if `blockWhenFull` is ever set to `true`.
- [ ] 3.3 Attach an `IAsyncLogEventSinkMonitor` and log `DroppedMessagesCount` at shutdown. Verify: unit test overflows the buffer and asserts a non-zero dropped count is reported.
- [ ] 3.4 Confirm the queue drains at exit. Verify: unit test logs a batch through a host built by `AddFileLogging`, disposes the host, and asserts every event reached the file — this test fails against `AddSerilog`'s default `dispose: false`, which discards the queue instead of draining it.

## 4. Runtime verbosity

- [ ] 4.1 Introduce a `LoggingLevelSwitch` owned by the logging component and use it as the minimum level. Verify: unit test writes at debug with the switch at information and asserts nothing is written.
- [ ] 4.2 Map `trace`, `debug`, `info`, `warn` and `error` case-insensitively to Serilog's `Verbose`, `Debug`, `Information`, `Warning` and `Error`, falling back to `Information` on an unrecognised value with a warning naming what was found. Verify: unit tests for all five values, for a differently-cased one, and for an invalid one; Serilog's own spellings such as `Verbose` are not accepted and must hit the fallback.
- [ ] 4.3 Register an `IHostedService` from `AddFileLogging` that takes the initial level from `SettingsStore.Current` and updates the switch on `Changed`, which is raised only from `Save`. Verify: unit test changes the level through the settings store and asserts output at the new level appears without re-creating the logger.
- [ ] 4.4 Correct `LoggingConfig.LogLevel`'s doc comment to name exactly the five accepted values, replacing "or other custom-defined levels", now that this change is what gives the property meaning. Verify: the comment lists the same five values the mapping in 4.2 accepts.
- [ ] 4.5 Confirm no second gate sits in front of the switch. Verify: unit test raises the level mid-run and asserts a previously suppressed statement now writes, and that `ILogger<T>.IsEnabled` agrees with the switch before and after.

## 5. Logging conventions

- [ ] 5.1 Document in `CLAUDE.md` that transcript text and API key values must never be logged, and that lengths, categories and outcomes are logged instead. Verify: the rule is written down before any change that handles transcripts is implemented.
- [ ] 5.2 Document in `CLAUDE.md` that hot-path statements need no `IsEnabled` guard (a suppressed call costs about 0.1 µs) and that Serilog's static `Log` is never used — the configured logger is always passed explicitly. Verify: both rules are written down before change 4 adds the first audio-path log statements.
