## Why

A dictation app fails in places the user cannot see: a hotkey that never fires, a microphone that
returns silence, a Gemini call that 429s. Without a log on disk, every one of those is an unactionable
"it didn't work" report.

## What Changes

- Configure Serilog writing to `~/.pisum-whisper/logs/pisum-whisper.log`.
- Roll on file size using `logMaxFileSizeMb` from settings, retaining at most 10 rotated files.
- Sweep files older than `logRetentionDays` at startup — size-based rolling alone never removes an
  old, small file, so both mechanisms are needed.
- Expose the log level through a `LoggingLevelSwitch` so changing it in settings takes effect
  immediately, with no restart. This is the direct analogue of the reference's reloadable
  `EnvFilter`, and it is the point of the feature: a user reproducing a bug can raise the level to
  `debug` mid-session.
- Add a console sink under `#if DEBUG` only.
- Expose the resolved log directory so the settings window can show it and open it later.

Reference: `W:\github-pisum-transcript\src-tauri\src\logging.rs`.

## Capabilities

### New Capabilities
- `file-logging`: diagnostic output is written to disk, rotated by size, expired by age, and its verbosity is changeable at runtime.

### Modified Capabilities
_None._

## Impact

Depends on `add-settings-store` for `LoggingConfig`. Every later change logs through the abstraction
this one registers, so landing it early keeps `ILogger<T>` available throughout rather than
retrofitted.

## Non-goals

- No Logging settings tab — that ships with `add-settings-window`.
- No "Open Log Folder" button; the path is exposed here, the button is UI.
- No remote log shipping, no crash reporting, no telemetry.
