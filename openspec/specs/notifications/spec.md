# notifications Specification

## Purpose

The only way this application can tell the user anything. A dictation is started from a global hotkey
while the user is working in another application, and the process has no window of its own: the tray
icon reports a state and not a reason, and the log file has to be opened to be read. So a failure
that is merely written down is one the user experiences as nothing happening at all — a rejected API
key, an exhausted quota and a microphone that will not open are indistinguishable from a hotkey that
did not register.

What is shown is decided by two different intentions that one setting must not conflate. Silencing
status chatter is a preference; consenting to hear nothing about a rejected key is not, so failures
are forced and status messages are suppressible, and the split is a property of the call rather than
of the transport underneath it.

The constraints on showing one are sharper than they look, and each of them is a way of getting this
wrong. This application pastes text at the cursor in whatever the user is typing in, so a
notification that takes focus does not merely look wrong — it removes the target the next dictation
would be delivered to. Two of the places a notification is raised run on the thread that dispatches
hotkey edges, where the next edge may be the release that ends a hold-to-record dictation, so
presenting one may not make that thread wait. And a notification is drawn on top of whatever the user
is doing, including a screen being shared or presented, which makes it a wider disclosure than the
log file: the rule against writing down a transcript, an API key or the user's clipboard applies here
with more force rather than less.

## Requirements

### Requirement: The user is told when a dictation fails
The application SHALL show the user the title and message it already produces for a failed dictation,
without the user opening a log file. A dictation is started from a global hotkey while the user is
working in another application, and the only surface this process has is a tray icon that reports a
state and not a reason — so a failure that is merely written down is a failure the user experiences
as nothing happening at all.

#### Scenario: A dictation fails
- **WHEN** a dictation fails for any reason other than the user quitting
- **THEN** the user is shown the failure's title and message

#### Scenario: A recording cannot be started
- **WHEN** the microphone cannot be opened and no recording begins
- **THEN** the user is shown the failure, and the hotkey remains able to start the next recording

#### Scenario: The transcript was copied but not pasted
- **WHEN** a delivery leaves the transcript on the clipboard without pasting it
- **THEN** the user is told the text can be pasted manually

#### Scenario: The user quits during a dictation
- **WHEN** a dictation is abandoned because the application is shutting down
- **THEN** nothing is shown, because the user asked for it

### Requirement: Failures are shown regardless of the user's preference, status messages are not
The application SHALL show every failure whether or not the user has enabled tray notifications, and
SHALL show status messages only when they have. Silencing status chatter and consenting to hear
nothing about a rejected API key are different intentions, and one setting must not conflate them.

#### Scenario: Notifications are disabled and a dictation fails
- **WHEN** the user has turned tray notifications off and a dictation fails
- **THEN** the failure is still shown

#### Scenario: Notifications are disabled and a status message is raised
- **WHEN** the user has turned tray notifications off and the hotkey is pressed during a transcription
- **THEN** nothing is shown

#### Scenario: Notifications are enabled and a status message is raised
- **WHEN** the user has tray notifications on and a recording is stopped automatically at the maximum duration
- **THEN** the user is told the recording was stopped and is being transcribed

#### Scenario: The preference is changed while the application is running
- **WHEN** the user changes the preference and a status message is raised afterwards
- **THEN** the new preference decides whether it is shown, without the application being restarted

### Requirement: A notification never takes focus from the application the user is working in
The application SHALL show a notification without activating itself and without moving keyboard focus
away from whatever window has it. This application's entire purpose is to paste text at the cursor in
another application; a notification that steals focus does not merely look wrong, it takes away the
target the next dictation would be delivered to.

#### Scenario: A notification is shown while the user is typing elsewhere
- **WHEN** a notification is shown while another application has keyboard focus
- **THEN** that application still has keyboard focus afterwards

#### Scenario: A notification is shown during a dictation
- **WHEN** a notification is shown between a recording ending and its transcript being pasted
- **THEN** the paste still reaches the application the user was working in

### Requirement: Showing a notification never delays the hotkey
The application SHALL NOT block the thread that reports hotkey edges while showing a notification.
Two of the places a notification is raised run on the thread that dispatches hotkey edges, and the
next edge on that thread may be the release that ends a hold-to-record dictation.

#### Scenario: A notification is raised from a hotkey edge
- **WHEN** the hotkey is pressed during a transcription and a status message is raised
- **THEN** the next hotkey edge is reported without waiting for the notification to appear or dismiss

### Requirement: A notification goes away on its own
The application SHALL dismiss each notification without the user acting on it, and SHALL NOT require
a click to clear one. The user is working in another application and may not be looking at the
screen; a notification that waits to be dismissed becomes an obstruction over whatever they are doing.

#### Scenario: A notification is left alone
- **WHEN** a notification has been shown and the user does nothing
- **THEN** it disappears on its own

#### Scenario: The application quits with a notification on screen
- **WHEN** the user quits while a notification is still shown
- **THEN** the notification is removed and the process exits

### Requirement: Several notifications do not obscure one another
The application SHALL place concurrent notifications so that each remains readable, and SHALL bound
how many are shown at once. Failures can arrive together — a transcription failure followed by a
delivery failure — and notifications drawn on top of each other convey less than one of them alone.

#### Scenario: A second notification arrives while the first is still shown
- **WHEN** two notifications are shown at the same time
- **THEN** both are readable and neither covers the other

#### Scenario: More notifications arrive than can be shown at once
- **WHEN** more notifications are raised than the application shows concurrently
- **THEN** the most recent are shown and the oldest are removed

### Requirement: A notification never reveals a transcript, an API key, or clipboard contents
The application SHALL NOT include transcript text, API key values, or the contents of the user's
clipboard in any notification. A notification is drawn on top of whatever the user is doing,
including a screen being shared or presented, so it is a wider disclosure than the log file the same
rule already protects.

#### Scenario: A dictation fails after the audio was transcribed
- **WHEN** a failure occurs at a point where a transcript exists
- **THEN** the notification describes the failure without quoting the transcript

#### Scenario: A configured key is rejected
- **WHEN** a provider rejects the configured API key
- **THEN** the notification reports an authentication failure without including the key

#### Scenario: The clipboard could not be restored
- **WHEN** the user's previous clipboard contents could not be put back
- **THEN** the notification says so without quoting what those contents were

### Requirement: A first launch is announced
The application SHALL tell the user it is running and needs configuring the first time it starts. A
tray-only application that starts silently, files its icon into a hidden overflow, and does nothing
until a hotkey is pressed gives a new user nothing to act on.

#### Scenario: The application starts for the first time
- **WHEN** the application starts and no settings file existed
- **THEN** the user is shown a welcome message telling them to configure a provider

#### Scenario: The application starts subsequently
- **WHEN** the application starts and settings already existed
- **THEN** no welcome message is shown

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
