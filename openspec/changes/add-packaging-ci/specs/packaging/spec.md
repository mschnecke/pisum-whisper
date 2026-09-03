## Purpose

Turns the built solution into something a person can install and launch: a versioned installer for
Windows x64 and macOS Apple Silicon, produced and published by continuous integration, and reaching
users through a package manager on each platform.

## ADDED Requirements

### Requirement: An installable artifact exists for each supported platform

The build SHALL produce, for each release, one installer per supported platform: a Windows Installer
package for `win-x64` and a macOS installer package for `osx-arm64`. Each SHALL contain the
application and every runtime dependency it needs, so that installing it on a machine with no .NET
runtime present yields a working application.

The artifacts SHALL be named so that the platform and the version are readable from the file name
alone.

#### Scenario: Installing on a machine with no .NET runtime

- **WHEN** the Windows installer is run on a clean Windows 11 x64 machine with no .NET runtime installed
- **THEN** the application installs and launches, reaching the tray without a runtime prompt

#### Scenario: Installing on a clean Apple Silicon Mac

- **WHEN** the macOS installer is run on a clean Apple Silicon machine with no .NET runtime installed
- **THEN** the application installs to `/Applications` and launches, reaching the menu bar without a runtime prompt

#### Scenario: The artifact names carry platform and version

- **WHEN** a release's assets are listed
- **THEN** each file name contains the release version and identifies its platform

### Requirement: The macOS application presents itself as a menu-bar application

The macOS artifact SHALL install an application bundle that declares itself an agent, so the
operating system gives it no Dock icon and no menu bar of its own, matching the behaviour the
application already requests at runtime.

The bundle SHALL declare a stable reverse-DNS identifier, and SHALL declare the reason it needs the
microphone so that the system's permission prompt is attributed to this application and states its
purpose.

#### Scenario: No Dock icon

- **WHEN** the installed application is launched
- **THEN** it appears in the menu bar, and no Dock icon and no application menu appear for it

#### Scenario: The microphone prompt names the application

- **WHEN** a dictation is started for the first time after installation
- **THEN** the system's microphone prompt is attributed to Pisum Whisper and states why it needs the microphone

#### Scenario: The bundle identity matches the one the application already writes

- **WHEN** the installed application registers itself to start at login
- **THEN** the identifier in the launch agent it writes is the same identifier the bundle declares

### Requirement: The macOS install leaves the application launchable without a Gatekeeper detour

The artifacts are not code-signed and not notarized, which is a recorded decision rather than an
omission. The macOS installer SHALL therefore leave the installed application in a state where a
user can launch it from Finder directly, without being told the application is damaged or from an
unidentified developer, and without having to right-click and choose Open or run a command in a
terminal.

#### Scenario: First launch after a download and install

- **WHEN** the installer is downloaded through a browser, run, and the installed application is opened from Finder
- **THEN** the application launches, and no Gatekeeper warning is shown

#### Scenario: First launch after installing through the package manager

- **WHEN** the application is installed through the Homebrew cask and opened from Finder
- **THEN** the application launches, and no Gatekeeper warning is shown

### Requirement: The Windows install registers the application with the system

The Windows installer SHALL place a Start-menu shortcut for the application, and that shortcut SHALL
carry an application user model identity, so that a future notification transport and the taskbar
have a stable identity to attach to.

The installer SHALL register the application for the system's installed-programs list, so that it can
be found and removed the way any other installed program is, and SHALL support unattended
installation so a package manager can drive it.

#### Scenario: A Start-menu entry appears

- **WHEN** the installer completes
- **THEN** a Start-menu shortcut for the application exists, and launching it starts the application in the tray

#### Scenario: The shortcut carries an application identity

- **WHEN** the installed shortcut's properties are inspected
- **THEN** it carries the application user model identity

#### Scenario: Unattended installation

- **WHEN** the installer is run unattended with no user interaction
- **THEN** it completes without prompting and returns a success result

### Requirement: Uninstalling removes the application and keeps the user's data

Uninstalling SHALL remove the installed application, its Start-menu shortcut on Windows and its
bundle on macOS. It SHALL NOT remove the user's settings file or log directory, because those hold
API keys and presets the user entered and a reinstall is not a request to discard them.

A separate, explicit purge SHALL be offered on macOS through the package manager for a user who does
want the data gone.

#### Scenario: Uninstall on Windows

- **WHEN** the application is uninstalled from the installed-programs list
- **THEN** the application and its Start-menu shortcut are gone, and the settings file and log directory remain

#### Scenario: Uninstall on macOS

- **WHEN** the application is uninstalled through the package manager
- **THEN** the application bundle is gone, and the settings file and log directory remain

#### Scenario: Explicit purge on macOS

- **WHEN** the user asks the package manager to remove the application together with its data
- **THEN** the settings file, the log directory and the launch agent are all removed

### Requirement: Every artifact reports the version it was released as

The application, its installers and the package-manager definitions SHALL all report the same
version for a given release, so that a bug report naming a version identifies one build.

The version SHALL be derived from the release tag rather than maintained separately in each artifact.

#### Scenario: Versions agree across the artifacts

- **WHEN** a release is built from a version tag
- **THEN** the installer file names, the installed application's reported version, and the package-manager definitions all carry that same version

### Requirement: Continuous integration builds and tests both platforms

Every proposed change SHALL be built and tested on both supported platforms before it can be merged.
The build SHALL be treated as failed if it produces any compiler warning, because warnings are
already errors in this solution.

The test run SHALL exclude only those tests that require a person at the machine; every other test
SHALL run on both platforms.

#### Scenario: A change that builds and passes

- **WHEN** a pull request is opened
- **THEN** the solution is built and the test suite is run on both Windows and macOS, and the result is reported on the pull request

#### Scenario: A change that introduces a warning

- **WHEN** a pull request introduces a compiler warning
- **THEN** the build fails

#### Scenario: Tests needing a person are not run

- **WHEN** the test suite runs in continuous integration
- **THEN** the tests gated on a real microphone, clipboard or keyboard are skipped rather than failed

### Requirement: Continuous integration proves the installers can still be built

The installers SHALL be built on every proposed change, not only at release time, so that a change
breaking the packaging is caught when it is made rather than when a release is attempted.

#### Scenario: A change that breaks packaging

- **WHEN** a pull request changes the application in a way that stops an installer being assembled
- **THEN** the pull request's checks fail

### Requirement: A tagged release publishes both installers

Pushing a version tag SHALL produce a published release carrying both installers as downloadable
assets, and SHALL do so without a person running a build by hand.

If either platform's artifact cannot be produced, the release SHALL NOT be published carrying only
the other one.

#### Scenario: A version tag is pushed

- **WHEN** a version tag is pushed
- **THEN** a release is published carrying the Windows and macOS installers as assets

#### Scenario: One platform fails to build

- **WHEN** one platform's artifact fails to build during a release
- **THEN** no release is published, and the failure is reported

### Requirement: A published release reaches the package managers

A published release SHALL be offered through a package manager on each platform: a Homebrew cask for
macOS and a Chocolatey package for Windows, each updated to the released version and each verifying
the artifact it downloads against a checksum recorded at release time.

#### Scenario: The macOS package manager is updated

- **WHEN** a release is published
- **THEN** the Homebrew cask is updated to that version with the checksum of the published artifact

#### Scenario: The Windows package manager is updated

- **WHEN** a release is published
- **THEN** the Chocolatey package is published at that version, referencing the published artifact and its checksum

#### Scenario: A tampered download is refused

- **WHEN** the artifact a package manager downloads does not match the recorded checksum
- **THEN** the installation is refused

### Requirement: The consequences of shipping unsigned are documented where the user meets them

Because the artifacts are neither signed nor notarized, the user SHALL be told, in the place where
they install the application, what to expect: on Windows that the installer will raise a reputation
warning and how to proceed past it, and on macOS that the permission the application depends on must
be granted again after an update, because the system has no stable identity to recognise the new
build by.

#### Scenario: The Windows warning is documented

- **WHEN** a user reads the installation instructions for Windows
- **THEN** the reputation warning is described together with the steps to continue past it

#### Scenario: The macOS re-grant is documented

- **WHEN** a user reads the installation instructions or the package manager's notes for macOS
- **THEN** they are told that Accessibility must be granted after installing, and granted again after each update
