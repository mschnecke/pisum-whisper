# settings-window Specification

## Purpose

The application's one editing surface for its own configuration: a window reached from the tray icon
that offers every setting the application reads — the transcription providers and their keys, the
prompt presets, the hotkey binding, the audio format, the logging options and the general options.
Without it the only editor is a hand-written `~/.pisum-whisper.json`, which makes the product
unusable by anyone but its author.

It owns the editing model rather than the settings themselves: an edit is kept without the user
confirming it, there is no OK, Cancel or Apply, and edits reach the running application without a
restart. Persistence, defaulting and change notification belong to `settings-persistence`, which this
capability writes through.

## Requirements

### Requirement: Every setting is editable from a window reached through the tray
The application SHALL provide a settings window reachable from the tray icon, and that window SHALL
offer every setting the application reads. Until it exists the only editor is a hand-written JSON
file, which makes the product unusable by anyone but its author; a setting the application honours
but the window omits is the same problem in miniature.

#### Scenario: The window is opened from the tray
- **WHEN** the user chooses Settings from the tray icon's menu
- **THEN** the settings window is shown and brought to the front

#### Scenario: The window is already open when it is asked for again
- **WHEN** the user asks for Settings while the window is open but behind another application
- **THEN** the same window is brought to the front, and no second window is created

#### Scenario: The settings on offer are surveyed
- **WHEN** the window is open
- **THEN** the transcription providers, the prompt presets, the hotkey, the audio format, the logging
  options and the general options are all reachable within it

### Requirement: The application keeps running when the window is closed
The application SHALL treat closing the settings window as hiding it, and SHALL keep running with its
tray icon and its hotkey. This is a tray-only application: if closing its one window ended the
process, every visit to settings would end in an unintended quit.

#### Scenario: The window is closed
- **WHEN** the user closes the settings window
- **THEN** the window disappears
- **AND** the application is still running, with its tray icon present

#### Scenario: A dictation is performed after the window is closed
- **WHEN** the user closes the settings window and then presses the hotkey
- **THEN** the dictation proceeds as it would have before the window was ever opened

#### Scenario: The application is quit while the window is open
- **WHEN** the user chooses Quit while the settings window is open
- **THEN** the window closes and the process exits

### Requirement: An edit is kept without the user being asked to save it
The application SHALL persist an edit without the user confirming it, and SHALL offer no OK, Cancel
or Apply. A tray utility is visited rarely and briefly, and an edit lost to a forgotten Apply button
is indistinguishable to the user from a setting that does not work.

#### Scenario: A setting is changed and the window is closed
- **WHEN** the user changes a setting and closes the window
- **THEN** the change is still in effect

#### Scenario: A setting is changed and the application is restarted
- **WHEN** the user changes a setting, quits the application, and starts it again
- **THEN** the change is still in effect

#### Scenario: Text is typed into a field
- **WHEN** the user types a value into a text field and stops
- **THEN** the value is persisted once the typing has stopped, without the user pressing anything

#### Scenario: The window is closed immediately after an edit
- **WHEN** the user changes a setting and closes the window before the edit has been persisted
- **THEN** the edit is persisted rather than discarded

### Requirement: An edit reaches the running application without a restart
The application SHALL apply an edited setting to the running application, and SHALL say so where a
setting cannot take effect until the next launch. Every other field applying instantly teaches the
user that they all do, so the exceptions have to be named where they are edited.

#### Scenario: The hotkey is changed
- **WHEN** the user records a new hotkey
- **THEN** the new combination starts a dictation and the previous one no longer does, without the
  application being restarted

#### Scenario: The log level is changed
- **WHEN** the user changes the log level
- **THEN** subsequent log output is written at the new level, without the application being restarted

#### Scenario: The active preset is changed
- **WHEN** the user activates a different preset
- **THEN** the tray icon's tooltip names the newly active preset
- **AND** the next dictation is transcribed with that preset's instructions

#### Scenario: A setting takes effect only at the next launch
- **WHEN** the user edits a setting that cannot be applied to the running application
- **THEN** the window states that the setting takes effect at the next launch

#### Scenario: A provider is added during a session
- **WHEN** the user adds an enabled provider and then dictates
- **THEN** that provider is among those used, without the application being restarted

### Requirement: An API key is protected in the window and in the log
The application SHALL mask an API key by default, SHALL reveal it only when the user asks, and SHALL
never write a key value to the log file. The settings file holds credentials in plaintext already;
the window is displayed on a screen other people can see, and the log file is one click from a button
this same window offers.

#### Scenario: A configured key is displayed
- **WHEN** the window shows a provider that has an API key
- **THEN** the key is masked

#### Scenario: The user asks to see a key
- **WHEN** the user activates the reveal control for a key
- **THEN** the key is shown in full
- **AND** it can be masked again

#### Scenario: A key is entered, tested and listed against
- **WHEN** the user types an API key, tests it, and lists the models it may use
- **THEN** no log entry produced by any of those actions contains the key value, at any log level

#### Scenario: A key is rejected by the service
- **WHEN** a provider action fails and the failure is reported in the window
- **THEN** the reported text does not contain the key value

### Requirement: A key can be checked before a dictation depends on it
The application SHALL let the user verify an API key from the window and see the outcome there, and
SHALL offer the models that key may use. A key is a long opaque string typed or pasted by hand; the
alternative to checking it here is discovering it is wrong by losing a dictation that has already
been spoken.

#### Scenario: A working key is tested
- **WHEN** the user tests a provider whose key and model are usable
- **THEN** the window reports that the connection succeeded

#### Scenario: An unusable key is tested
- **WHEN** the user tests a provider whose key is rejected
- **THEN** the window reports the failure and what kind of failure it was

#### Scenario: The service cannot be reached
- **WHEN** the user tests a provider and the service cannot be reached
- **THEN** the window reports that, rather than reporting a rejected key

#### Scenario: The models for a key are listed
- **WHEN** the user asks for the models available to a key
- **THEN** the window offers those models for selection
- **AND** offers a default choice that needs no selection

#### Scenario: The model list is refreshed
- **WHEN** the user asks to refresh the model list after changing the key
- **THEN** the list is fetched again rather than served from the earlier key's result

### Requirement: Presets can be created, edited, activated and deleted
The application SHALL let the user manage the prompt presets, SHALL show which preset is active and
which are built in, and SHALL refuse to delete a built-in preset. The preset decides what the model
is told to do with the user's speech, so it is the setting that changes the product's output most
directly; the built-ins are the two the application falls back to and cannot be allowed to vanish.

#### Scenario: The presets are listed
- **WHEN** the user views the presets
- **THEN** each preset's name is shown, the built-in ones are marked as built in, and the active one
  is marked as active

#### Scenario: A preset is created
- **WHEN** the user adds a preset with a name and a prompt
- **THEN** it appears in the list and can be activated

#### Scenario: A preset is offered with a blank name or a blank prompt
- **WHEN** the user attempts to add or save a preset whose name or whose prompt is empty or only
  whitespace
- **THEN** it is not saved
- **AND** a preset with an empty prompt never becomes selectable

#### Scenario: A preset is edited
- **WHEN** the user edits a preset's name or prompt
- **THEN** the change is kept, including for a built-in preset

#### Scenario: A user preset is deleted
- **WHEN** the user deletes a preset they created
- **THEN** it is removed from the list

#### Scenario: A built-in preset is offered for deletion
- **WHEN** the user views a built-in preset
- **THEN** no means of deleting it is offered

#### Scenario: The active preset is deleted
- **WHEN** the user deletes the preset that is currently active
- **THEN** another preset becomes active, and the application still has an active preset

### Requirement: A new hotkey is chosen by pressing it
The application SHALL let the user set the hotkey by pressing the combination they want, SHALL require
at least one modifier, SHALL let the attempt be abandoned, and SHALL warn when the chosen combination
is one the operating system is likely to claim. A hotkey typed as text is a hotkey the user cannot be
sure their keyboard produces; a hotkey with no modifier makes the key unusable everywhere else.

Recording SHALL NOT outlive the user's attention to it. While a combination is being recorded the
configured hotkey does not work, so a recorder left open is a dictation tool with no way to start a
dictation, and it says nothing about being in that state.

#### Scenario: A combination is recorded
- **WHEN** the user starts recording and presses a modifier together with a key
- **THEN** that combination becomes the hotkey, and the window shows it

#### Scenario: A key is pressed without a modifier
- **WHEN** the user presses a key with no modifier held
- **THEN** it is not accepted as the hotkey, and recording continues

#### Scenario: Recording is abandoned
- **WHEN** the user presses Escape while recording
- **THEN** recording stops and the hotkey is left as it was

#### Scenario: The window loses focus while recording
- **WHEN** the user starts recording and then switches to another application
- **THEN** recording stops and the hotkey is left as it was
- **AND** the configured hotkey starts a dictation again

#### Scenario: An unnameable key is pressed
- **WHEN** the user presses a key the application cannot record
- **THEN** the window says the key is not supported, and recording continues

#### Scenario: A system combination is chosen
- **WHEN** the recorded combination is one the operating system is likely to own
- **THEN** the window warns that it may not work reliably
- **AND** the combination is still accepted

#### Scenario: The hotkey is not being observed
- **WHEN** the application could not start observing keys system-wide
- **THEN** the window says so, and does not offer to record a combination it could never capture

#### Scenario: The hotkey is pressed while recording
- **WHEN** the user is recording a new combination and presses the currently configured hotkey
- **THEN** no dictation starts

### Requirement: A value with limits cannot be set outside them
The application SHALL confine every bounded setting to its bounds, whatever the user enters. These
values reach a recording watchdog, a log rotation size and a retention sweep; a zero or a negative
one is not a preference but a defect the user typed in by accident.

#### Scenario: A value above the maximum is entered
- **WHEN** the user enters a value greater than a setting's maximum
- **THEN** the setting takes its maximum

#### Scenario: A value below the minimum is entered
- **WHEN** the user enters a value smaller than a setting's minimum
- **THEN** the setting takes its minimum

#### Scenario: A value that is not a number is entered
- **WHEN** the user clears a numeric field or enters text in it
- **THEN** the setting takes a usable value rather than an empty or invalid one

### Requirement: The log files can be found from the window
The application SHALL show where its log files are written and SHALL offer to open that location.
Diagnosing a failed dictation means reading the log, and a path the user has to be told over a support
channel is a path they will mistype.

#### Scenario: The log location is shown
- **WHEN** the user views the logging settings
- **THEN** the directory the log files are written to is shown in full

#### Scenario: The log folder is opened
- **WHEN** the user chooses to open the log folder
- **THEN** the operating system's file browser opens at that directory

#### Scenario: The log folder cannot be opened
- **WHEN** opening the log folder fails
- **THEN** the application keeps running and the window remains usable
