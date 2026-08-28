## ADDED Requirements

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
