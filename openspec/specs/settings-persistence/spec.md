# settings-persistence Specification

## Purpose

The single owner of the application's configuration: loading `~/.pisum-whisper.json` at startup,
defaulting every absent property so an older or partial file still loads, repairing the parts that
can go stale (missing built-in presets, an `activePresetId` that resolves to nothing), persisting
changes atomically, and notifying subscribers so a change takes effect without a restart. Every
other capability — the hotkey binding, the Gemini keys and model, the active preset's system prompt,
the recording mode, the log level — reads its configuration from here.

## Requirements

### Requirement: Settings persist across runs
The application SHALL store its settings as JSON at `~/.pisum-whisper.json` and reload them on
startup.

#### Scenario: Settings survive a restart
- **WHEN** a setting is changed and the application is restarted
- **THEN** the changed value is in effect after restart

#### Scenario: Settings are written in camelCase
- **WHEN** settings are written to disk
- **THEN** every property name in the file is camelCase

### Requirement: Settings schema
The settings file SHALL contain the following properties with the stated defaults. This shape is
fixed by this change because five later changes read it, and changing it afterwards means touching
all of them.

| Property | Type | Default |
|---|---|---|
| `startWithSystem` | boolean | `true` |
| `showTrayNotifications` | boolean | `true` |
| `hotkey` | `{ modifiers: string[], key: string }` | `Ctrl`+`Shift`+`Space`, or `Cmd`+`Shift`+`Space` on macOS |
| `audioFormat` | `"opus"` \| `"wav"` | `"opus"` |
| `presets` | `Preset[]` | the built-in presets |
| `activePresetId` | string | `"de-transcribe"` |
| `providers` | `ProviderConfig[]` | empty |
| `recordingMode` | `"holdToRecord"` \| `"toggle"` | `"holdToRecord"` |
| `maxRecordingDurationSecs` | integer | `600` |
| `loggingConfig` | `{ logLevel, logMaxFileSizeMb, logRetentionDays }` | `"info"`, `1`, `7` |

`Preset` is `{ id, name, systemPrompt, isBuiltin }`, where `isBuiltin` defaults to `false`.
`ProviderConfig` is `{ id, apiKey, model, enabled }`, where `model` is nullable and `enabled`
defaults to `true`.

#### Scenario: Fields belonging to the reference are absent
- **WHEN** settings are written to disk
- **THEN** no `transcriptionMode`, `whisperConfig` or `providerType` property is present, the first two having gone with local inference and the third with the decision that Gemini is the only provider

### Requirement: Every setting has a default
Every property of the settings root and of its nested configuration objects SHALL have a default
value, so that a settings file missing any property loads successfully rather than failing.

The identity fields of list elements are exempt and SHALL be required: a preset's `id`, `name` and
`systemPrompt`, and a provider's `id` and `apiKey`. An element missing one of them cannot be
defaulted into anything meaningful, and the reference rejects such a file rather than materialising
an element with empty values.

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

#### Scenario: A list element is missing a required field
- **WHEN** a settings file contains a preset or a provider that omits one of its required fields
- **THEN** loading fails with a parse error naming the file, rather than producing an element with empty values

### Requirement: A save never leaves the file partially written
The application SHALL write the settings file atomically, so that an interrupted save leaves either
the complete previous file or the complete new one.

#### Scenario: The process is interrupted mid-save
- **WHEN** the process stops while settings are being written
- **THEN** the file on disk is either the complete previous content or the complete new content, never truncated

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

### Requirement: The active preset can be changed
The application SHALL allow the active preset to be changed to any existing preset, and SHALL reject
a change to an id that matches no preset.

#### Scenario: Switching to an existing preset
- **WHEN** the active preset is set to the id of an existing preset
- **THEN** that preset becomes active and the change is persisted

#### Scenario: Switching to an unknown preset
- **WHEN** the active preset is set to an id matching no preset
- **THEN** the request is rejected with an error naming the id
- **AND** the previously active preset remains active

### Requirement: Presets are added and updated by id
The application SHALL treat saving a preset as an upsert keyed on `id`: an unknown id is appended,
and a known id has its name and system prompt updated. Saving SHALL NOT change whether a preset is
built-in.

#### Scenario: Saving a preset with an unknown id
- **WHEN** a preset whose id matches no existing preset is saved
- **THEN** it is appended to the preset list

#### Scenario: Editing a built-in preset's prompt
- **WHEN** a preset whose id matches a built-in preset is saved with a changed prompt
- **THEN** the prompt is updated and the preset remains marked built-in
- **AND** the edit survives the next load rather than being replaced by the built-in text

### Requirement: Built-in presets cannot be deleted
The application SHALL refuse to delete a preset marked as built-in.

#### Scenario: Deleting a built-in preset
- **WHEN** deletion of a built-in preset is requested
- **THEN** the request is rejected with an explanatory error and the preset list is unchanged

#### Scenario: Deleting a preset that does not exist
- **WHEN** deletion is requested for an id matching no preset
- **THEN** the request is rejected with an error naming the id

### Requirement: Settings changes are observable
The application SHALL notify interested components when settings change, so they can re-apply
without a restart.

#### Scenario: A setting is saved
- **WHEN** settings are saved
- **THEN** the in-memory cache is updated and subscribers are notified with the new values
