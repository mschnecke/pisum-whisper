## ADDED Requirements

### Requirement: Settings persist across runs
The application SHALL store its settings as JSON at `~/.pisum-whisper.json` and reload them on
startup.

#### Scenario: Settings survive a restart
- **WHEN** a setting is changed and the application is restarted
- **THEN** the changed value is in effect after restart

#### Scenario: Settings are written in camelCase
- **WHEN** settings are written to disk
- **THEN** every property name in the file is camelCase

### Requirement: Every setting has a default
Every property SHALL have a default value, so that a settings file missing any property loads
successfully rather than failing.

#### Scenario: A property is absent from the file
- **WHEN** a settings file omits one or more properties
- **THEN** the missing properties take their default values and no error is raised

#### Scenario: The settings file does not exist
- **WHEN** the application starts with no settings file present
- **THEN** a file containing the complete defaults is written
- **AND** the run is reported as a first launch

#### Scenario: The settings file is unreadable
- **WHEN** the settings file exists but contains invalid JSON
- **THEN** the failure is surfaced with the underlying parse error rather than silently overwritten

### Requirement: Built-in presets are restored
The application SHALL merge any missing built-in presets into the loaded settings, so a built-in
preset added in a later version appears for existing users.

#### Scenario: A built-in preset is missing from the file
- **WHEN** settings are loaded and a built-in preset id is not present
- **THEN** that built-in preset is added to the preset list

#### Scenario: A built-in preset was edited by the user
- **WHEN** settings are loaded and a built-in preset id is present with user-modified content
- **THEN** the user's version is kept and not overwritten

### Requirement: The active preset always resolves
The application SHALL guarantee that `activePresetId` refers to an existing preset, repairing and
re-persisting the settings when it does not.

#### Scenario: The active preset id refers to nothing
- **WHEN** settings are loaded and `activePresetId` matches no preset
- **THEN** the first built-in preset becomes active
- **AND** the corrected settings are written back to disk

#### Scenario: The active preset is deleted
- **WHEN** the user deletes the preset that is currently active
- **THEN** the first remaining preset becomes active

### Requirement: Built-in presets cannot be deleted
The application SHALL refuse to delete a preset marked as built-in.

#### Scenario: Deleting a built-in preset
- **WHEN** deletion of a built-in preset is requested
- **THEN** the request is rejected with an explanatory error and the preset list is unchanged

### Requirement: Settings changes are observable
The application SHALL notify interested components when settings change, so they can re-apply
without a restart.

#### Scenario: A setting is saved
- **WHEN** settings are saved
- **THEN** the in-memory cache is updated and subscribers are notified with the new values
