## ADDED Requirements

### Requirement: The running application is represented in the system tray
The application SHALL show an icon in the Windows notification area or the macOS menu bar for as long
as the process is running, and that icon SHALL be the only thing representing the application while
no window is open. This is what makes a process with no window, no taskbar button and no Dock icon
something the user can find, judge and quit.

#### Scenario: The application is launched
- **WHEN** the application starts
- **THEN** an icon appears in the system tray or menu bar
- **AND** the process remains running with no window shown

#### Scenario: The application is running with no dictation in progress
- **WHEN** the application is idle
- **THEN** the icon is still present, showing the idle appearance

### Requirement: The icon reports what the application is doing about a dictation
The application SHALL give each of the three dictation states — idle, recording, transcribing — its
own icon appearance, and SHALL update the icon as the state changes. Three appearances, not two: an
icon that shows "recording" while the recording is already being uploaded tells a user in toggle mode
to keep speaking into a closed microphone, and one that shows "idle" tells them nothing is happening
immediately before the hotkey refuses a second dictation.

#### Scenario: Recording begins
- **WHEN** a recording starts
- **THEN** the icon changes to the recording appearance

#### Scenario: Recording ends and transcription begins
- **WHEN** the recording stops and the dictation proceeds to transcription
- **THEN** the icon changes to the transcribing appearance, distinct from both the idle and the recording appearance

#### Scenario: A dictation finishes
- **WHEN** the dictation completes, fails, or is cancelled
- **THEN** the icon returns to the idle appearance

#### Scenario: A recording is discarded as too short
- **WHEN** a recording is discarded without being transcribed
- **THEN** the icon returns to the idle appearance without having shown the transcribing appearance

### Requirement: The three states are distinguishable without relying on colour
The application SHALL make the three icon appearances differ in shape, not in colour alone. Colour is
unavailable on one of the two target platforms — macOS renders a menu bar icon from its silhouette
and discards its colours — and is unreliable for a colour-blind user on the other, so an icon set
separated only by hue conveys nothing on macOS and less than intended on Windows.

#### Scenario: The icon is rendered without colour
- **WHEN** the three appearances are reduced to silhouettes
- **THEN** each of the three remains distinguishable from the other two

#### Scenario: The icon is seen at its smallest rendered size
- **WHEN** the icon is displayed at the smallest size the platform uses
- **THEN** the three appearances remain distinguishable from one another

### Requirement: The icon is legible on both light and dark system backgrounds
The application SHALL present an icon that remains legible whether the tray or menu bar it sits in is
light or dark, and SHALL do so without the user configuring anything. The user does not choose the
background their tray icon is drawn on, and an icon that disappears into it fails the only job this
capability has.

#### Scenario: The system is set to a light appearance
- **WHEN** the tray or menu bar is light
- **THEN** the icon is legible against it in all three states

#### Scenario: The system is set to a dark appearance
- **WHEN** the tray or menu bar is dark
- **THEN** the icon is legible against it in all three states

#### Scenario: The system appearance changes while the application is running
- **WHEN** the user switches between light and dark while the application is running
- **THEN** the icon remains legible, without the application being restarted

### Requirement: The active preset is shown on the icon's tooltip
The application SHALL show the name of the active preset in the icon's tooltip, and SHALL update it
when the active preset changes. Which preset is active decides what the model is told to do with the
user's speech, so it changes the product's output; the tooltip is where that is visible without
opening anything.

#### Scenario: The user hovers over the icon
- **WHEN** the pointer rests on the tray icon
- **THEN** a tooltip identifies the application and names the active preset

#### Scenario: The active preset changes
- **WHEN** the active preset is changed
- **THEN** the tooltip names the new preset without the application being restarted

#### Scenario: The tooltip is shown
- **WHEN** a tooltip is presented
- **THEN** it contains the preset's name and never the preset's system prompt

### Requirement: The icon offers Settings and Quit
The application SHALL give the icon a menu holding a Settings entry and a Quit entry, separated from
one another. Quit SHALL end the application. These are the only two actions a tray-only process must
expose to be usable at all: a way to change what it does, and a way to stop it.

#### Scenario: The menu is opened
- **WHEN** the user opens the icon's menu
- **THEN** it offers Settings and Quit, with a separator between them

#### Scenario: Quit is chosen
- **WHEN** the user chooses Quit
- **THEN** the application shuts down and the process exits

### Requirement: The icon is removed when the application exits
The application SHALL remove its icon from the tray or menu bar as it exits, leaving nothing behind
that outlives the process. An icon that survives the process it represents is worse than no icon: it
claims the application is running when it is not, and it accumulates one entry per launch.

#### Scenario: The application exits
- **WHEN** the application shuts down
- **THEN** the icon is removed from the tray or menu bar

#### Scenario: The application is relaunched
- **WHEN** the application is quit and immediately relaunched
- **THEN** exactly one icon is present

### Requirement: Reporting the state never delays a dictation
The application SHALL update the icon without blocking the dictation pipeline, and SHALL present the
state changes of one dictation in the order they occurred. The state is reported from whichever
thread the dictation is running on, and a presentation step that made that thread wait would put work
on the path between the user releasing the hotkey and their words arriving.

#### Scenario: A dictation reports its states
- **WHEN** a dictation moves through recording, transcribing and idle
- **THEN** the icon shows those appearances in that order

#### Scenario: A dictation completes quickly
- **WHEN** a dictation's states change faster than the icon can be redrawn
- **THEN** the icon settles on the state the dictation actually ended in

#### Scenario: The application is quit mid-dictation
- **WHEN** the application is quit while a dictation is in progress
- **THEN** the application exits cleanly and no state update outlives the icon it would have updated
