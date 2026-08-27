## 1. Sink setup

- [ ] 1.1 Add Serilog and `Serilog.Sinks.File` to central package management and register Serilog behind `Microsoft.Extensions.Logging` in the composition root, after settings load. Verify: run the app and confirm `~/.pisum-whisper/logs/pisum-whisper.log` is created and contains a startup line.
- [ ] 1.2 Resolve and expose the log directory through a service, creating it if absent. Verify: unit test against a temp home asserts the resolved path and that the directory is created.
- [ ] 1.3 Make setup failure non-fatal: if the directory cannot be created or written, report and continue. Verify: unit test with an unwritable path asserts no exception escapes and the failure is reported.
- [ ] 1.4 Enable Serilog `SelfLog` to the debug console under `#if DEBUG` only. Verify: misconfigure a sink temporarily and confirm the diagnostic appears, then revert.
- [ ] 1.5 Add a console sink under `#if DEBUG` only. Verify: debug build prints to console; release build produces no console output.

## 2. Size and age bounds

- [ ] 2.1 Configure size-based rolling from `logMaxFileSizeMb` with `retainedFileCountLimit` of 10. Verify: integration test sets a 1 KB limit, writes until it rolls, and asserts a second file exists.
- [ ] 2.2 Assert the retained-file cap. Verify: integration test rolls more than ten times and asserts at most ten rolled files remain.
- [ ] 2.3 Implement the startup age sweep deleting `*.log*` files older than `logRetentionDays`. Verify: unit test creates files with backdated timestamps either side of the boundary and asserts only the expired ones are deleted.

## 3. Runtime verbosity

- [ ] 3.1 Introduce a `LoggingLevelSwitch` owned by the logging component and use it as the minimum level. Verify: unit test writes at debug with the switch at information and asserts nothing is written.
- [ ] 3.2 Map the `logLevel` string to a Serilog level, falling back to `information` on an unrecognised value with a warning. Verify: unit tests for each valid value and one invalid value.
- [ ] 3.3 Subscribe to the settings change event and update the switch. Verify: unit test changes the level through the settings store and asserts output at the new level appears without re-creating the logger.
- [ ] 3.4 Confirm nothing caches `IsEnabled` across a level change. Verify: unit test raises the level mid-run and asserts a previously suppressed statement now writes.

## 4. Logging conventions

- [ ] 4.1 Document in `CLAUDE.md` that transcript text and API key values must never be logged, and that lengths, categories and outcomes are logged instead. Verify: the rule is written down before any change that handles transcripts is implemented.
