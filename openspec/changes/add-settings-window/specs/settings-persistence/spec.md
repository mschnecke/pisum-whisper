## ADDED Requirements

### Requirement: Cached settings are replaced, never modified in place
The application SHALL publish a settings change by replacing the settings object it serves from
memory, rather than by modifying the object other components are already reading. A component reading
settings while another writes them SHALL observe either the previous values in full or the new values
in full, and SHALL never observe a collection being changed underneath it or a moment in which the
active preset id matches no preset.

This is a rule about the cache, not about the file. Settings are read on whichever thread needs them
— the transcription path resolves the active preset's prompt by scanning the preset list on a pooled
thread, while preset edits arrive from the settings window — and a list changed during that scan
fails the read outright rather than returning a stale answer. A dictation the user has already spoken
is lost as a result, which is the most expensive way this application can fail.

#### Scenario: A preset is added while another component is reading settings
- **WHEN** a preset is saved while another component is reading the preset list
- **THEN** the reader completes, seeing either the list without the new preset or the list with it

#### Scenario: A preset is deleted during a transcription
- **WHEN** the user deletes a preset while a transcription is resolving the active preset's prompt
- **THEN** the transcription is unaffected and completes

#### Scenario: The active preset is deleted
- **WHEN** the active preset is deleted and another preset becomes active
- **THEN** at no point do the settings served from memory name an active preset that does not exist

#### Scenario: A reader holds settings across a write
- **WHEN** a component reads the settings object and a write completes before it reads a field
- **THEN** the values it reads are internally consistent with one another

#### Scenario: A rejected write changes nothing
- **WHEN** a write is rejected — deleting a built-in preset, or activating an unknown preset
- **THEN** the settings served from memory are unchanged and no subscriber is notified
