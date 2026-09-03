## MODIFIED Requirements

### Requirement: The registration is reconciled with the setting
The application SHALL make the login registration agree with `startWithSystem` when it starts and
whenever settings are saved, and SHALL write only when the two disagree. The setting is what the user
asked for; the registration is the state of the machine, and the two can drift — a settings file
edited by hand, an entry removed by another tool, or an entry that is still present but names an
executable this application no longer runs from, all leave a preference that claims something untrue.

Agreement SHALL mean that the registration names the running executable, and not merely that some
registration exists. A registration naming a different executable is drift like any other: it will
launch something other than this application at login while the setting reports that starting at
login is arranged, which is the one kind of drift the user cannot discover by reading the setting.

#### Scenario: The setting is enabled and no registration exists
- **WHEN** the application starts with `startWithSystem` enabled and no login registration present
- **THEN** the registration is created

#### Scenario: The setting is disabled and a registration exists
- **WHEN** the application starts with `startWithSystem` disabled and a login registration present
- **THEN** the registration is removed

#### Scenario: The setting and the registration already agree
- **WHEN** the application starts and the registration already names the running executable
- **THEN** nothing is written

#### Scenario: The registration names a different executable
- **WHEN** the application starts with `startWithSystem` enabled and a login registration that names an executable other than the running one
- **THEN** the registration is rewritten to name the running executable

#### Scenario: A registration naming a different executable is removed when the setting is off
- **WHEN** the application starts with `startWithSystem` disabled and a login registration that names an executable other than the running one
- **THEN** the registration is removed

#### Scenario: The user changes the setting while the application is running
- **WHEN** the user turns the setting on or off in the settings window
- **THEN** the registration is brought into agreement without the application being restarted

#### Scenario: The registration was removed by something else
- **WHEN** the registration is removed outside the application and the application is restarted with the setting still enabled
- **THEN** the registration is restored

## ADDED Requirements

### Requirement: A corrected registration is distinguishable in the log from a new one
The application SHALL record which of the two it did when it writes a registration: created one where
none existed, or corrected one that named something else. A registration that had to be corrected is
evidence that the machine was launching the wrong executable at login, and reporting it identically to
a first-time enable would leave the log unable to say that anything had ever been wrong.

#### Scenario: A registration naming a different executable is corrected
- **WHEN** the application rewrites a registration that named a different executable
- **THEN** the log records that the registration was repointed, in terms that distinguish it from a registration being created
