## ADDED Requirements

### Requirement: A settings write that fails to reach disk is reported, not silently lost
The application SHALL tell the user when an edit, a preset add, save, activation or deletion could
not be written to the settings file, and SHALL leave the affected tab showing what is actually
persisted rather than the edit that failed to save. An edit that is kept without confirmation, as
this window's own model requires, is trustworthy only if a failure to keep it is visible; a window
that looks like it worked when the file was never written is worse than one that visibly errors.

#### Scenario: A debounced field edit cannot be written
- **WHEN** the user edits a setting on any tab and the quiet-window commit cannot write the settings
  file
- **THEN** the user is told the settings could not be saved, regardless of the tray-notification
  preference

#### Scenario: A preset cannot be added, saved, activated or deleted
- **WHEN** the user adds, saves, activates or deletes a preset and the write cannot reach the
  settings file
- **THEN** the user is told the settings could not be saved, regardless of the tray-notification
  preference

#### Scenario: A failed preset save shows the persisted text, not the failed edit
- **WHEN** the user edits a preset's name or prompt, saves it, and the write fails
- **THEN** the preset's displayed name and prompt revert to what is actually stored, rather than
  continuing to show the edit as if it had been saved

#### Scenario: A failed preset add keeps the typed fields for retry
- **WHEN** the user adds a preset and the write fails
- **THEN** the name and prompt the user typed remain in the add fields, and no preset is added to the
  list
