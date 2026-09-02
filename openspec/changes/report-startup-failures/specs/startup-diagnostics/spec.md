## ADDED Requirements

### Requirement: A failure that prevents startup is shown to the user
The application SHALL tell the user, on screen, when it cannot finish starting, and the message SHALL
name what failed. It SHALL do so on a surface that does not depend on the application having started,
because every surface the application owns — its tray icon, its window, its notifications — comes into
existence after the point at which these failures happen.

This is the whole reason the capability exists. The application is tray-only: it has no window, no
taskbar entry, and in a release build no console. A failure before the tray icon appears is therefore
indistinguishable, to the person who launched it, from the application never having been launched at
all — and it is most often launched from a login item, where nobody is watching for it.

#### Scenario: The settings file cannot be read
- **WHEN** the settings file exists but cannot be parsed
- **THEN** the user is shown a message naming the settings file, and the application exits reporting failure

#### Scenario: The settings file cannot be created
- **WHEN** no settings file exists and one cannot be written
- **THEN** the user is shown a message naming the settings file, and the application exits reporting failure

#### Scenario: The application cannot assemble itself
- **WHEN** the application cannot construct the services it needs to run
- **THEN** the user is shown a message saying it could not start, and the application exits reporting failure

#### Scenario: A resource the tray icon needs is missing
- **WHEN** an image the tray icon is drawn from cannot be loaded
- **THEN** the user is shown a message saying it could not start, and the application exits reporting failure

#### Scenario: Startup succeeds
- **WHEN** the application starts normally
- **THEN** nothing is shown, and the application runs in the tray as it always has

#### Scenario: The message cannot be shown
- **WHEN** the user cannot be told, because the operating system offers no way to
- **THEN** the failure is still recorded and the application still exits reporting failure

### Requirement: A failure that prevents startup is recorded before the user is told
The application SHALL write the failure to its log, and that record SHALL be complete on disk before
the process ends. The message shown to the user SHALL point at the log rather than reproduce it.

The two halves answer different questions. What is shown has to be short enough to read while logging
in and has to name something the user can act on; what is written has to carry enough for whoever is
diagnosing it. Ordering matters because the log is written asynchronously: a process that exits
without finishing that write produces a log file that is unchanged by the very failure that ended it,
which is the reported defect this capability was raised for.

#### Scenario: A startup failure is recorded
- **WHEN** the application fails to start and its log can be written
- **THEN** the log contains an entry describing the failure

#### Scenario: The user is pointed at the log
- **WHEN** the user is shown a startup failure
- **THEN** the message names where the log is to be found

#### Scenario: The log cannot be written at the same time
- **WHEN** the application fails to start and its log directory is also unusable
- **THEN** the user is still shown the failure

### Requirement: A settings file that cannot be read is never replaced
The application SHALL NOT start on default settings when a settings file exists but cannot be read,
and SHALL NOT overwrite or repair that file. It SHALL leave the file exactly as it found it.

Refusing to start is the deliberate choice here, and it is made because of what the file holds. The
settings file holds the user's API keys in plaintext. Starting on defaults would present a settings
window backed by an empty configuration, and the first thing that window saved would replace the
user's keys with nothing — turning a file that a text editor could have repaired into one that cannot
be recovered. A failure the user has to fix is better than a silent, unrecoverable loss.

#### Scenario: The settings file is unreadable
- **WHEN** the settings file exists but cannot be parsed
- **THEN** the application does not start, and the file is left byte for byte as it was

#### Scenario: The file is repaired
- **WHEN** the user corrects the file and starts the application again
- **THEN** it starts normally on the user's own settings

### Requirement: A degraded start is reported without the user going looking
The application SHALL tell the user when it has started but something it needs is not working. It
SHALL do so once a surface exists to show it on, rather than at the moment the condition is
discovered, and it SHALL still start.

These conditions are not failures to start and must not be treated as such — a dictation tool with no
hotkey is still worth having open, because its settings window is where the hotkey is fixed. But they
are also not conditions the user can be expected to discover: one of them removes the log that would
have explained it, and the other makes the application silently inert in exactly the way that looks
like it is not running.

#### Scenario: The log cannot be written to
- **WHEN** the application starts and its log directory could not be created
- **THEN** the user is told, and the application continues running

#### Scenario: Keys cannot be observed
- **WHEN** the application starts and the configured binding is not being observed
- **THEN** the user is told, and the application continues running with its settings reachable

#### Scenario: Both are true at once
- **WHEN** the application starts with neither a usable log nor an observable binding
- **THEN** the user is told about both

#### Scenario: Nothing is wrong
- **WHEN** the application starts with everything working
- **THEN** the user is told nothing

### Requirement: A degraded start is reported even when status messages are silenced
The application SHALL report a degraded start regardless of whether the user has turned off status
notifications.

The preference exists to silence chatter — that a recording auto-stopped, that a transcription is
already running. These are not that. Someone who has silenced status messages has not asked to be
kept from knowing that their hotkey does not work, and the consequence of guessing wrong is an
application that appears to do nothing at all with no way to find out why.

#### Scenario: Status notifications are turned off and the start is degraded
- **WHEN** the user has turned off status notifications and the application starts degraded
- **THEN** the user is still told

#### Scenario: Status notifications are turned off and the start is clean
- **WHEN** the user has turned off status notifications and nothing is wrong
- **THEN** the user is told nothing, as before
