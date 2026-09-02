## ADDED Requirements

### Requirement: A change in whether the binding is observed is reported
The application SHALL make a change in whether the configured binding is being observed available to
the rest of the application as it happens, so that a binding which stops working can be reported to
the user without the user opening a window.

Today this is knowable only by asking, and only one place asks: the settings window's Hotkey tab. That
is the wrong audience. A user whose hotkey has stopped working has no reason to open a settings
window — from where they sit, the application is doing nothing, and the tray icon says it is idle,
which is true and useless. The tab keeps its banner for the user who is already there; this is for the
one who is not.

Access can be withdrawn long after the application has started, and on macOS is routinely absent when
it first starts, so this covers both the state observation begins in and every later change to it.

#### Scenario: Access is withdrawn while the application is running
- **WHEN** permission to observe keys is withdrawn after observation has started
- **THEN** the user is told, without having opened the settings window

#### Scenario: Access was never granted
- **WHEN** the application starts and has never been permitted to observe keys
- **THEN** the user is told once the application has a surface to tell them on

#### Scenario: Observation starts normally
- **WHEN** the binding is observed from startup and keeps being observed
- **THEN** the user is told nothing

#### Scenario: The same state is reported again
- **WHEN** the state is published again without having changed
- **THEN** the user is told once, not twice

#### Scenario: Observation begins later than expected
- **WHEN** observation could not be confirmed at startup but begins afterwards
- **THEN** no failure is reported to the user, because the binding now works

### Requirement: Reporting a change in observation never delays the binding
Reporting that the binding can or cannot be observed SHALL NOT delay the reporting of the binding's
own edges, and SHALL NOT be done from the point at which keys are matched.

This is the same constraint the capability already places on everything else it does, restated because
this change introduces a new consumer of it. Whatever is told about a change in observation may be
slow — it draws on screen — and the place where keys are matched is bounded by the operating system,
which removes a hook that takes too long without saying so. A report that is made from there costs the
user their hotkey in the course of telling them their hotkey is fine.

#### Scenario: A slow consumer is told about a change
- **WHEN** something that takes a noticeable time to respond is told the observation state changed
- **THEN** presses and releases of the binding continue to be reported on time

#### Scenario: The state changes while the binding is held
- **WHEN** observation ends while the binding is held down
- **THEN** the release owed for that press is still reported, as it always was
