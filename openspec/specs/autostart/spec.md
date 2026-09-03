# autostart Specification

## Purpose

Whether the application is running at all when the user reaches for it. A dictation tool is reached
by a global hotkey and has no window, so one that must be launched by hand each morning is one whose
hotkey does nothing for most of the day — and the user has no way to notice that except by pressing
it and getting silence. Registering to start at login is what makes the hotkey answerable, and being
able to unregister again is what keeps that a preference rather than an imposition.

The registration lives in the operating system and the preference lives in the settings file, which
means there are two records of one intention and they can drift apart. A settings file edited by
hand, an entry removed by another tool, or a first launch that never opened the settings window all
leave a preference claiming something untrue. So this capability is written as a reconciliation
rather than a toggle: the setting is what the user asked for, the registration is the state of the
machine, and the application makes the second agree with the first — at startup and whenever settings
are saved, writing only when they disagree. That covers the first launch, the hand-edited file and
the registration some other tool removed through one path, where a toggle wired to a switch in the
window would cover none of them.

Two boundaries hold regardless. The registration is made for the user running the application and
never for the machine, because the preference is per-user and stored in a per-user file, and a
dictation tool has no business starting for someone who never asked for it — which also means it must
never need elevation. And a registration that cannot be read or written is a reason to lose
autostart, not a reason to lose the dictation hotkey: a machine policy or a locked key leaves the
application starting normally, with the reason recorded.

## Requirements

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

### Requirement: A corrected registration is distinguishable in the log from a new one
The application SHALL record which of the two it did when it writes a registration: created one where
none existed, or corrected one that named something else. A registration that had to be corrected is
evidence that the machine was launching the wrong executable at login, and reporting it identically to
a first-time enable would leave the log unable to say that anything had ever been wrong.

#### Scenario: A registration naming a different executable is corrected
- **WHEN** the application rewrites a registration that named a different executable
- **THEN** the log records that the registration was repointed, in terms that distinguish it from a registration being created
