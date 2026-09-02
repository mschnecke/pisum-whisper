# global-hotkey Specification

## Purpose

The one system-wide keyboard observation this process is allowed to run, and the only source of the
press-and-release signal the whole product hangs on: hold the configured binding to record, release
it to transcribe. Neither platform's hotkey registration API reports a release, so this capability
observes keys raw and matches the configured combination itself — which also means it sees every
keystroke on the machine and must never record one. It owns the vocabulary of key and modifier names
a binding is written in, the capture mode the settings window's hotkey recorder reuses rather than
opening a second observation, the conflict warning against known operating-system shortcuts, and the
permission state on macOS, where observing keys needs a grant the user cannot have given on a first
launch.

## Requirements

### Requirement: The configured binding is observed system-wide
The application SHALL observe the key combination configured in `hotkey.modifiers` and `hotkey.key`
regardless of which application has keyboard focus, without that application's cooperation and
without the application itself holding focus.

#### Scenario: Another application has focus
- **WHEN** the configured combination is pressed while a different application has keyboard focus
- **THEN** the application observes it

#### Scenario: The application has no window
- **WHEN** the application is running with no window shown
- **THEN** the configured combination is still observed

### Requirement: Both edges of the binding are reported
The application SHALL report a press when the configured combination becomes held, and a release
when it stops being held. Hold-to-record cannot be built on a press-only signal, which is the reason
this capability exists rather than deferring to the operating system's hotkey registration APIs.

#### Scenario: The combination is pressed
- **WHEN** every key of the configured combination is held
- **THEN** a press is reported exactly once

#### Scenario: The combination is released
- **WHEN** the combination has been reported as pressed and stops being held
- **THEN** a release is reported exactly once

#### Scenario: A different combination is pressed
- **WHEN** a key combination other than the configured one is pressed
- **THEN** neither a press nor a release is reported

### Requirement: Holding the binding reports a single press
The application SHALL report exactly one press for one physical hold of the combination, however
long it is held. Both target platforms deliver repeated key-down events while a key is held, so
without this a single hold would report an unbounded number of presses.

#### Scenario: The combination is held down
- **WHEN** the configured combination is held for several seconds
- **THEN** exactly one press is reported, and no release is reported until the combination is released

### Requirement: Releasing any key of the binding ends the hold
The application SHALL report a release when the main key **or** any configured modifier is released,
not only when the main key is released. The user does not release a chord's keys simultaneously, and
treating the main key as the only terminator leaves the hold active while the remaining key repeats
into the focused application.

#### Scenario: A modifier is released first
- **WHEN** the combination has been reported as pressed and a configured modifier is released while the main key is still held
- **THEN** a release is reported

#### Scenario: The main key is released first
- **WHEN** the combination has been reported as pressed and the main key is released while the modifiers are still held
- **THEN** a release is reported

#### Scenario: The remaining keys are released afterwards
- **WHEN** a release has been reported and the combination's other keys are then released
- **THEN** no further release is reported

### Requirement: Modifier matching is side-agnostic and exact
The application SHALL treat the left and right instance of a modifier as equivalent, SHALL ignore
the state of Caps Lock, Num Lock, Scroll Lock and mouse buttons when matching, and SHALL require
that exactly the configured modifiers are held — no more and no fewer.

#### Scenario: The right-hand modifier is used
- **WHEN** the combination is pressed using the right-hand instance of a configured modifier
- **THEN** it matches as though the left-hand instance had been used

#### Scenario: A lock key is engaged
- **WHEN** the combination is pressed while Caps Lock, Num Lock or Scroll Lock is on
- **THEN** it matches

#### Scenario: A mouse button is held
- **WHEN** the combination is pressed while a mouse button is held down
- **THEN** it matches

#### Scenario: An additional modifier is held
- **WHEN** the configured combination is pressed together with a modifier that is not configured
- **THEN** it does not match

#### Scenario: A binding with no modifiers
- **WHEN** the binding configures no modifiers and its key is pressed while a modifier is held
- **THEN** it does not match

### Requirement: The matched binding does not reach the focused application
The application SHALL consume both edges of the binding's main key so that the application holding
focus does not receive them. The binding is held for the duration of a dictation, and a default of
Ctrl+Shift+Space is a live shortcut in common editors; without this, dictating delivers seconds of
repeated keystrokes to whatever has focus.

Modifier keys SHALL NOT be consumed. Applications derive modifier state from the key events they
receive, and withholding a modifier release leaves them believing it is still held.

#### Scenario: The binding is pressed over a text editor
- **WHEN** the configured combination is pressed and released while a text editor has focus
- **THEN** the editor receives neither the key press nor the key release of the main key

#### Scenario: Modifier keys continue to reach the focused application
- **WHEN** the configured combination is pressed and released
- **THEN** the focused application receives the press and release of each modifier key

#### Scenario: An unmatched key is pressed
- **WHEN** a key that is not part of the configured combination is pressed
- **THEN** it reaches the focused application unchanged

### Requirement: A release is reported for every press
The application SHALL report a release for any binding it has reported as pressed, including when
the observation stops before the physical key release is seen. Session lock, user switching and the
operating system withdrawing the application's input access all consume the release, and a press
without a matching release leaves every consumer of this capability believing the binding is still
held.

#### Scenario: Observation stops while the binding is held
- **WHEN** the binding has been reported as pressed and observation stops for any reason
- **THEN** a release is reported

#### Scenario: The application shuts down while the binding is held
- **WHEN** the binding has been reported as pressed and the application shuts down
- **THEN** a release is reported before shutdown completes

### Requirement: The binding changes without a restart
The application SHALL adopt a new binding as soon as the settings are saved, without restarting the
application and without any interval in which no binding is observed.

#### Scenario: The binding is changed
- **WHEN** `hotkey.modifiers` or `hotkey.key` is changed and the settings are saved
- **THEN** the new combination is observed and the previous one is not, without restarting

#### Scenario: The binding is changed while it is held
- **WHEN** the binding is reported as pressed and the settings are saved with a different binding
- **THEN** a release is reported for the previous binding before the new one takes effect

### Requirement: An unusable binding does not leave the application without a hotkey
The application SHALL fall back to the default combination and report the reason when the configured
binding names a modifier or key it does not recognise. A tray-only application that silently has no
hotkey gives the user no way to discover why nothing happens.

The application SHALL NOT rewrite the settings file in response; the fallback applies to the running
session only.

#### Scenario: The configured key is not recognised
- **WHEN** the settings name a key that is not in the recognised vocabulary
- **THEN** the default combination is observed instead
- **AND** the unrecognised name is reported

#### Scenario: A configured modifier is not recognised
- **WHEN** the settings name a modifier that is not in the recognised vocabulary
- **THEN** the default combination is observed instead
- **AND** the unrecognised name is reported

#### Scenario: The settings file is left alone
- **WHEN** a fallback has been applied
- **THEN** the settings file still contains the binding the user configured

### Requirement: Key and modifier names are a defined vocabulary
The application SHALL recognise key names case-insensitively, covering the letters `A`–`Z`, the
digits `0`–`9`, `F1`–`F12`, the arrow keys, the numpad keys, and the common editing and punctuation
keys, including the reference's short aliases such as `ESC` for `ESCAPE`, `DEL` for `DELETE`, `PGUP`
for `PAGEUP`, and the punctuation characters themselves such as `-` for `MINUS`.

The application SHALL recognise the modifier names `ctrl` and `control`; `alt`; `shift`; and `meta`,
`super`, `win`, `cmd` and `command`, treating the members of each group as equivalent.

The application SHALL also resolve a key to a single canonical name, so that a combination captured
from the keyboard can be written back into settings.

#### Scenario: A key name differs in case
- **WHEN** a key is named in lower case, upper case or mixed case
- **THEN** it resolves to the same key

#### Scenario: An alias is used
- **WHEN** a key is named by one of its aliases
- **THEN** it resolves to the same key as its full name

#### Scenario: Equivalent modifier names
- **WHEN** a modifier is named `cmd`, `command`, `win`, `super` or `meta`
- **THEN** all of them resolve to the same modifier

#### Scenario: A key is resolved to a name
- **WHEN** a key that belongs to the vocabulary is resolved to its canonical name
- **THEN** that name resolves back to the same key

#### Scenario: A key outside the vocabulary is resolved
- **WHEN** a key that does not belong to the vocabulary is resolved to a canonical name
- **THEN** the resolution reports that the key has no name, rather than inventing one

### Requirement: A combination can be captured from the keyboard
The application SHALL provide a capture mode that reports the next complete key combination the user
presses, expressed in names that can be written into settings. Because only one system-wide
observation may be active in the process, capture SHALL reuse the same observation rather than
establishing a second one.

While capture is active the configured binding SHALL NOT be matched, and neither a press nor a
release SHALL be reported for it. Capture SHALL end when a combination has been captured or when it
is cancelled, and normal matching SHALL resume in both cases.

#### Scenario: A combination is captured
- **WHEN** capture is active and the user presses a modifier combination and a key
- **THEN** the combination is reported as a set of modifier names and a key name

#### Scenario: The configured binding is pressed while capturing
- **WHEN** capture is active and the user presses the currently configured binding
- **THEN** it is captured, and no press or release of the binding is reported

#### Scenario: Capture is cancelled
- **WHEN** capture is active and is cancelled without a combination being pressed
- **THEN** no combination is reported and the configured binding is matched again

#### Scenario: A key with no name is pressed while capturing
- **WHEN** capture is active and the user presses a key outside the recognised vocabulary
- **THEN** the capture reports that the key cannot be named, rather than reporting a combination that cannot be persisted

### Requirement: Conflicts with known system hotkeys are reported, never blocked
The application SHALL report whether a binding matches a known operating-system shortcut, comparing
modifiers as an unordered set and ignoring case. The list is a heuristic and users have legitimate
reasons to override it, so the application SHALL accept and observe a conflicting binding.

The known shortcuts are Ctrl+Alt+Delete, Alt+Tab, Alt+F4, Win+L, Win+D, Win+E, Win+R, Win+Tab,
Ctrl+Shift+Escape, Cmd+Q, Cmd+W, Cmd+Tab, Cmd+Shift+3, Cmd+Shift+4, Cmd+Shift+5, Cmd+Space and
Ctrl+Space.

#### Scenario: A binding matches a known system shortcut
- **WHEN** a binding equal to one of the known shortcuts is checked
- **THEN** it is reported as conflicting

#### Scenario: The modifiers are given in a different order
- **WHEN** a binding lists the same modifiers as a known shortcut in a different order
- **THEN** it is reported as conflicting

#### Scenario: A conflicting binding is configured
- **WHEN** a binding reported as conflicting is saved
- **THEN** it is observed like any other binding

#### Scenario: A binding matches no known shortcut
- **WHEN** a binding that is not in the list is checked
- **THEN** it is reported as not conflicting

### Requirement: The application does not observe its own synthetic input
The application SHALL ignore key events it generated itself. The transcript is delivered by
simulating a paste keystroke, and a hook that observed its own simulated input would react to the
application's own output.

#### Scenario: The application simulates a keystroke
- **WHEN** the application simulates a key press or release
- **THEN** it is not matched against the binding and neither edge is reported

### Requirement: Missing input permission does not prevent startup
The application SHALL start, run, and report the reason when the operating system denies it the
access required to observe keys system-wide. On macOS this is the Accessibility grant, which cannot
be present on a first launch; refusing to start would leave the user with no application from which
to request it.

The application SHALL distinguish access that was never granted from access withdrawn while it was
running, because the two have different remedies.

#### Scenario: Access has not been granted
- **WHEN** the application starts and the operating system denies it access to observe keys
- **THEN** the application starts, no binding is observed, and the reason is reported

#### Scenario: Access is withdrawn while running
- **WHEN** access is withdrawn after the application has started
- **THEN** the application continues to run and reports that access was withdrawn, distinctly from access never having been granted

#### Scenario: The permission state is available to the rest of the application
- **WHEN** the binding cannot be observed for want of permission
- **THEN** that state can be queried, so it can be surfaced to the user

### Requirement: Observed keystrokes are never recorded
The application observes every keystroke on the machine in order to match one combination. It SHALL
NOT write the identity of any key that is not the configured binding to any log, at any verbosity
level, and SHALL NOT retain it after matching.

What the application MAY record is the configured binding itself, the edges of that binding, counts,
and outcomes.

#### Scenario: Typing while the application runs
- **WHEN** the user types text in another application at the most verbose log level
- **THEN** no record of the keys typed appears in the log

#### Scenario: The binding is pressed
- **WHEN** the configured combination is pressed at a verbosity level that records it
- **THEN** the log names the binding and the edge, and nothing about any other key

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
