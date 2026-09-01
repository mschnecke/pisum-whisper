## ADDED Requirements

### Requirement: The application can start itself at login
The application SHALL be able to register itself to launch when the user logs in, and to unregister
itself again. A dictation tool is reached by a global hotkey and has no window; one that must be
started by hand each morning is one whose hotkey does nothing for most of the day, and the user has
no way to notice that except by pressing it.

#### Scenario: Autostart is enabled
- **WHEN** the application registers itself to start at login
- **THEN** the registration exists and names the running executable

#### Scenario: Autostart is disabled
- **WHEN** the application unregisters itself
- **THEN** the registration no longer exists

#### Scenario: Autostart is enabled twice
- **WHEN** the application registers itself while it is already registered
- **THEN** the registration exists once, and the user has one entry rather than two

### Requirement: The registration is made for the current user only
The application SHALL register itself for the user who is running it, and SHALL NOT make a
registration that affects other users of the machine or that requires elevation. The setting is a
per-user preference stored in a per-user settings file, and a dictation tool has no business starting
for someone who never asked for it.

#### Scenario: Autostart is enabled by an unprivileged user
- **WHEN** a user without administrative rights enables autostart
- **THEN** the registration succeeds without prompting for elevation

### Requirement: The registration is reconciled with the setting
The application SHALL make the login registration agree with `startWithSystem` when it starts and
whenever settings are saved, and SHALL write only when the two disagree. The setting is what the user
asked for; the registration is the state of the machine, and the two can drift — a settings file
edited by hand, or an entry removed by another tool, leaves a preference that claims something untrue.

#### Scenario: The setting is enabled and no registration exists
- **WHEN** the application starts with `startWithSystem` enabled and no login registration present
- **THEN** the registration is created

#### Scenario: The setting is disabled and a registration exists
- **WHEN** the application starts with `startWithSystem` disabled and a login registration present
- **THEN** the registration is removed

#### Scenario: The setting and the registration already agree
- **WHEN** the application starts and the registration already matches the setting
- **THEN** nothing is written

#### Scenario: The user changes the setting while the application is running
- **WHEN** the user turns the setting on or off in the settings window
- **THEN** the registration is brought into agreement without the application being restarted

#### Scenario: The registration was removed by something else
- **WHEN** the registration is removed outside the application and the application is restarted with the setting still enabled
- **THEN** the registration is restored

### Requirement: A first launch honours the default
The application SHALL apply `startWithSystem` on its first launch as it would on any other, so that a
user who never opens the settings window gets the behaviour the default promises.

#### Scenario: The application starts for the first time
- **WHEN** the application starts, no settings file existed, and the default enables autostart
- **THEN** the login registration is created

### Requirement: A failure to register never stops the application from starting
The application SHALL continue to start when the login registration cannot be read or written, and
SHALL record why. A machine policy, a locked registry key, or an unwritable home directory is a
reason to lose autostart, not a reason to lose the dictation hotkey.

#### Scenario: The registration cannot be written
- **WHEN** the login registration cannot be created or removed
- **THEN** the application continues to start, and the reason is written to the log

#### Scenario: The registration cannot be read
- **WHEN** the current registration cannot be determined
- **THEN** the application continues to start, and the reason is written to the log
