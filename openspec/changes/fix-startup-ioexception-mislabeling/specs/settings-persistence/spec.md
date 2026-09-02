## ADDED Requirements

### Requirement: A settings write failure is reported the same way regardless of when it happens
The application SHALL raise the same kind of error for a settings file that cannot be written whether
the failure happens on the first launch's initial write or on a later save, and that error SHALL name
the settings file.

`SettingsStore` writes through the same private path on both occasions, so a caller reading the
failure — the startup dialog on first launch, a settings-window save afterwards — SHALL be able to
recognise a settings write failure as such regardless of which call reached it, rather than the
first-launch case arriving as a different, unwrapped exception type.

#### Scenario: The settings file cannot be written on first launch
- **WHEN** no settings file exists and the file cannot be written
- **THEN** the failure names the settings file and is of the same kind a later write failure would raise

#### Scenario: A later save cannot be written
- **WHEN** settings are saved after the application has started and the file cannot be written
- **THEN** the failure names the settings file and is of the same kind the first-launch failure would raise
