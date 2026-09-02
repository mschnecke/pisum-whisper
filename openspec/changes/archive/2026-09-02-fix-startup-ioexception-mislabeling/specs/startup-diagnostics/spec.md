## MODIFIED Requirements

### Requirement: A failure that prevents startup is shown to the user
The application SHALL tell the user, on screen, when it cannot finish starting, and the message SHALL
name what failed. It SHALL do so on a surface that does not depend on the application having started,
because every surface the application owns — its tray icon, its window, its notifications — comes into
existence after the point at which these failures happen.

This is the whole reason the capability exists. The application is tray-only: it has no window, no
taskbar entry, and in a release build no console. A failure before the tray icon appears is therefore
indistinguishable, to the person who launched it, from the application never having been launched at
all — and it is most often launched from a login item, where nobody is watching for it.

What failed SHALL be identified by where the failure actually occurred, not by the runtime type of
the exception it happened to raise. An exception type that a settings failure raises MAY also be
raised by a failure that has nothing to do with settings, and matching on type alone would mislabel
the second as the first.

#### Scenario: The settings file cannot be read
- **WHEN** the settings file exists but cannot be parsed
- **THEN** the user is shown a message naming the settings file, and the application exits reporting failure

#### Scenario: The settings file cannot be created
- **WHEN** no settings file exists and one cannot be written
- **THEN** the user is shown a message naming the settings file, and the application exits reporting failure

#### Scenario: The application cannot assemble itself
- **WHEN** the application cannot construct the services it needs to run
- **THEN** the user is shown a message saying it could not start, and the application exits reporting failure

#### Scenario: A resource the tray icon needs is missing
- **WHEN** an image the tray icon is drawn from cannot be loaded
- **THEN** the user is shown a message saying it could not start, and the application exits reporting failure

#### Scenario: A non-settings failure raises the same kind of exception a settings failure would
- **WHEN** startup fails for a reason unrelated to the settings file, and that failure raises an exception of a type also used to report a settings failure
- **THEN** the user is shown a message saying the application could not start, and nothing in the message names the settings file

#### Scenario: Startup succeeds
- **WHEN** the application starts normally
- **THEN** nothing is shown, and the application runs in the tray as it always has

#### Scenario: The message cannot be shown
- **WHEN** the user cannot be told, because the operating system offers no way to
- **THEN** the failure is still recorded and the application still exits reporting failure
