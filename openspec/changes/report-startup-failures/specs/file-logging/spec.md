## ADDED Requirements

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
