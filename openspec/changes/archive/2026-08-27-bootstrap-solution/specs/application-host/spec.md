## ADDED Requirements

### Requirement: Menu-bar-only process
The application SHALL run as a background process with no dock icon on macOS and no taskbar entry on
Windows, remaining alive with no window shown.

#### Scenario: Launched on macOS
- **WHEN** the application is launched on macOS
- **THEN** no icon appears in the Dock
- **AND** the process remains running with no window shown

#### Scenario: Launched on Windows
- **WHEN** the application is launched on Windows
- **THEN** no taskbar button appears
- **AND** no console window appears

### Requirement: Service composition root
The application SHALL build a single dependency-injection container at startup and resolve all
services from it. A missing or unsatisfiable registration MUST fail at startup, not at first use.

#### Scenario: Container is built at startup
- **WHEN** the application starts
- **THEN** the host builds the container and every registered singleton resolves successfully

#### Scenario: A dependency cannot be satisfied
- **WHEN** a registered service has an unsatisfiable dependency
- **THEN** the application fails during startup with a diagnostic naming the service
- **AND** it does not reach the point of showing a tray icon

### Requirement: Clean shutdown
The application SHALL release native resources on exit so that repeated launches do not leak
keyboard hooks or audio devices.

#### Scenario: User quits the application
- **WHEN** the user chooses Quit
- **THEN** hosted services are stopped, native handles are released, and the process exits with code 0

#### Scenario: Relaunch after quit
- **WHEN** the application is quit and immediately relaunched
- **THEN** it starts successfully without reporting a device or hook already in use

### Requirement: Verified platform stack
The application SHALL depend only on third-party libraries whose required behaviour has been
demonstrated on both win-x64 and osx-arm64. Each dependency below MUST be proven before code is
built on top of it.

#### Scenario: Global hook reports both key edges
- **WHEN** a key combination is pressed and released while another application has focus
- **THEN** SharpHook reports a press event and a release event on both platforms

#### Scenario: Simulated paste is accepted
- **WHEN** the SharpHook event simulator sends Ctrl+V, or Cmd+V on macOS, to a focused text editor
- **THEN** the editor pastes the clipboard contents

#### Scenario: Capture device opens at the requested format
- **WHEN** a capture device is opened at 48 kHz mono float32
- **THEN** the device opens on both platforms and delivers samples at that format regardless of the device's native rate

#### Scenario: Tray icon is visible and updatable
- **WHEN** a tray icon is created and its image is replaced at runtime
- **THEN** the icon appears in the Windows notification area and the macOS menu bar, and the replacement is reflected

#### Scenario: Encoded audio is well-formed
- **WHEN** captured audio is encoded to Ogg/Opus
- **THEN** the resulting file plays back correctly in a standard media player

#### Scenario: A dependency fails its demonstration
- **WHEN** any of the above cannot be demonstrated on a target platform
- **THEN** the named fallback is adopted and the affected design is revised before dependent work begins
