# file-logging Specification

## Purpose

The diagnostic record of a process the user cannot watch: a tray-only application whose hotkey,
microphone and Gemini calls all fail silently from the user's side. This capability writes structured
output to a file under `~/.pisum-whisper/logs/`, keeps that directory bounded by both size and age,
lets the verbosity be raised mid-session without a restart so a problem can be reproduced at `debug`,
and exposes the resolved path so the user can be shown where the log lives. Every other capability
logs through the abstraction registered here.

## Requirements

### Requirement: Diagnostics are written to disk
The application SHALL write structured log output to a file under
`~/.pisum-whisper/logs/`, creating the directory if it does not exist.

#### Scenario: First run with no log directory
- **WHEN** the application starts and the log directory does not exist
- **THEN** the directory is created and a log file is written

#### Scenario: The log directory cannot be created
- **WHEN** the log directory cannot be created or written to
- **THEN** the application continues to run and reports the failure, rather than failing to start

### Requirement: Logs are bounded by size
The application SHALL roll the log file when it exceeds the configured maximum size, retaining at
most ten log files in total, the active file included.

#### Scenario: The log file exceeds the size limit
- **WHEN** the active log file grows past `logMaxFileSizeMb`
- **THEN** it is rolled and subsequent output goes to a new file

#### Scenario: More than ten log files exist
- **WHEN** rolling would produce an eleventh file
- **THEN** the oldest file is removed

### Requirement: Logs are bounded by age
The application SHALL delete log files older than the configured retention period at startup.
Size-based rolling alone never removes an old, small file, so both bounds are required.

#### Scenario: An expired log file is present at startup
- **WHEN** the application starts and a log file is older than `logRetentionDays`
- **THEN** that file is deleted

#### Scenario: A log file is within the retention period
- **WHEN** the application starts and a log file is newer than `logRetentionDays`
- **THEN** that file is kept

### Requirement: Verbosity changes without restart
The application SHALL apply a change to the configured log level immediately, so that a user
reproducing a problem can raise verbosity mid-session.

#### Scenario: The user raises the log level
- **WHEN** `logLevel` is changed from `info` to `debug`
- **THEN** debug output appears in the log file without restarting the application

#### Scenario: The user lowers the log level
- **WHEN** `logLevel` is changed from `debug` to `warn`
- **THEN** informational and debug output stops appearing without restarting the application

### Requirement: The log location is discoverable
The application SHALL expose the resolved log directory so it can be shown to the user and opened.

#### Scenario: The log path is requested
- **WHEN** the log directory path is requested
- **THEN** the absolute resolved path is returned, whether or not any log file exists yet

### Requirement: A log directory that cannot be used is reported to the user
The application SHALL keep the reason its log directory could not be created available for as long as
the application runs, so that it can be reported to the user once there is a surface to report it on.

The capability already decides, correctly, that an unusable log directory is not a reason to refuse to
start: a dictation tool that will not launch because it cannot write a log is worse than one that
launches without one. What it does with the reason is the gap. It writes the reason through the
logger — the same logger that has just been built without a file behind it — so the one message
explaining why there is no log is itself written nowhere. In a release build, which has no console
either, nothing about it reaches anyone at all.

This also matters more than it looks, because it removes the fallback that everything else in the
application depends on. Every other failure this application can suffer is designed to end up in that
file, so a log that is silently not being written turns a diagnosable problem into an undiagnosable
one, and does it without saying so.

#### Scenario: The log directory cannot be created
- **WHEN** the log directory cannot be created at startup
- **THEN** the application starts, writes no log file, and the user is told that nothing is being logged

#### Scenario: The reason is available after startup
- **WHEN** the log directory could not be created and the application has finished starting
- **THEN** the reason is still available to be reported, rather than having been lost when it occurred

#### Scenario: The log directory is usable
- **WHEN** the log directory exists or is created successfully
- **THEN** the user is told nothing, and logging proceeds as before

#### Scenario: The failure is reported once
- **WHEN** the application starts with an unusable log directory
- **THEN** the user is told once, not once per component that notices
