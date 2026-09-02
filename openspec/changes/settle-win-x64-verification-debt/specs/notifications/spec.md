## ADDED Requirements

### Requirement: A notification raised before the application has finished starting does not prevent it from starting
The application SHALL start normally, and SHALL show notifications raised after it has started, even
when a notification was raised before its user interface existed and from a thread the interface will
never run on. The notification raised early MAY be shown once the interface is up or MAY be dropped;
if it is dropped, its loss SHALL be recorded in the log.

A dictation can begin before the interface exists: the hotkey is observed from the moment the host
starts, and the interface is initialised after that. A recording that fails to start in that window
raises a notification from the thread that reports hotkey edges, which is a pooled thread. Whatever
is raised there must not decide, for the life of the process, which thread the interface belongs to —
a notification lost is a small cost, an application that comes up without a working interface is not.

#### Scenario: A notification is raised from a background thread before the interface exists
- **WHEN** a notification is raised from a background thread before the application's interface has been initialised
- **THEN** the application still starts, its tray icon appears, and a notification raised afterwards is shown

#### Scenario: The early notification is accounted for
- **WHEN** a notification raised before the interface existed is not shown
- **THEN** the log records that it was dropped, naming its title and nothing else
