## ADDED Requirements

### Requirement: The window opens itself on a first launch
The application SHALL show the settings window, without being asked, the first time it runs. Every
other route to the window is a deliberate act — a tray menu item or a click on the icon — and a new
user has no reason to perform either: the application cannot transcribe anything until a provider key
is entered, and nothing on screen says so. Windows files a new tray icon into the hidden overflow and
a menu bar extra can be pushed under a notch, so "find the icon and open Settings" is not a
discoverable first step.

This does not change how the window closes. It hides on close as it always has, so dismissing it on a
first launch leaves the application running in the tray rather than quitting it.

#### Scenario: The application starts for the first time
- **WHEN** the application starts and no settings file existed
- **THEN** the settings window is shown and focused

#### Scenario: The application starts subsequently
- **WHEN** the application starts and settings already existed
- **THEN** no window is shown, and the application remains tray-only until asked

#### Scenario: The window is closed after a first launch
- **WHEN** the user closes the window that opened itself on a first launch
- **THEN** the window is hidden and the application continues running in the tray
