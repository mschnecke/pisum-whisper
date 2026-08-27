## 1. Spikes (do these first — they gate everything else)

- [ ] 1.1 Create a throwaway `spikes/` console project, outside the solution, referencing SharpHook 8.0.0. Verify: run on Windows, press and release Ctrl+Shift+Space while Notepad has focus, and confirm both a press and a release event are logged with the correct modifier mask.
- [ ] 1.2 Run the same spike on macOS. Verify: both edges logged; record whether the hook needs the main thread and whether a CFRunLoop is required. Note the Accessibility prompt behaviour.
- [ ] 1.3 Extend the spike with `EventSimulator` sending Ctrl+V (Cmd+V on macOS). Verify: put known text on the clipboard, focus a text editor, run the spike, and confirm the text is pasted on both platforms.
- [ ] 1.4 Set up a stable development code-signing identity for macOS and sign the spike binary. Verify: grant Accessibility once, rebuild, rerun, and confirm the grant persists without re-prompting.
- [ ] 1.5 Spike SoundFlow: open a capture device at 48 kHz mono float32 and record 5 seconds to a raw file. Verify: run on both platforms; confirm sample count matches the duration and that it works on a device whose native rate is not 48 kHz (e.g. a 44.1 kHz input).
- [ ] 1.6 Spike Avalonia 12.1 `TrayIcon`: show an icon, set a tooltip, swap the image on a timer, and add a two-item native menu. Verify: visible in the Windows notification area and the macOS menu bar; record whether the tooltip shows on macOS and whether template images are supported.
- [ ] 1.7 Spike Concentus 2.2.2 plus a minimal Ogg muxer: encode the 1.5 recording to `.opus`. Verify: the file plays back in VLC or ffplay; also check whether `Concentus.Oggfile` 1.0.7 compiles against Concentus 2.x.
- [ ] 1.8 Record each spike outcome in `design.md` under Open Questions, marking each resolved or replaced by its fallback. Verify: no Open Question remains unanswered.

## 2. Solution skeleton

- [ ] 2.1 Add `Directory.Build.props`: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`. Verify: `dotnet build` succeeds.
- [ ] 2.2 Add `Directory.Packages.props` with `ManagePackageVersionsCentrally=true` and the versions pinned in the design. Verify: a project referencing a package without a version builds.
- [ ] 2.3 Create `src/Pisum.Whisper.Core` and add it to `Pisum.Whisper.slnx` via `dotnet sln Pisum.Whisper.slnx add`. Verify: `dotnet build` succeeds.
- [ ] 2.4 Create `src/Pisum.Whisper.Platform`, referencing Core. Verify: builds.
- [ ] 2.5 Create `src/Pisum.Whisper.App` (Avalonia 12.1), referencing Core and Platform. Verify: builds.
- [ ] 2.6 Create `tests/Pisum.Whisper.Core.Tests` (MSTest, FakeItEasy, Shouldly) referencing Core, with one trivial passing test. Verify: `dotnet test` reports 1 passed.
- [ ] 2.7 Confirm the whole solution builds on both target platforms. Verify: `dotnet build -r win-x64` and `dotnet build -r osx-arm64` both succeed.

## 3. Composition root

- [ ] 3.1 Wire the generic host and `Microsoft.Extensions.DependencyInjection` in the App entry point. Verify: a debug log line at startup proves the container was built.
- [ ] 3.2 Enable eager validation of the container (validate on build, validate scopes) so a bad registration fails at startup. Verify: temporarily register a service with an unsatisfiable dependency and confirm startup throws naming that service; then revert.
- [ ] 3.3 Configure the Avalonia `AppBuilder` with `MacOSPlatformOptions { ShowInDock = false }` and add a stub tray icon with a Quit item. Verify: launch on macOS with no Dock icon; launch on Windows with no taskbar button or console window.
- [ ] 3.4 Implement clean shutdown: stop hosted services and dispose native handles on Quit. Verify: quit and immediately relaunch twice with no device- or hook-in-use error, and confirm exit code 0.

## 4. Documentation

- [ ] 4.1 Replace the placeholder "Status" section in `CLAUDE.md` with the real solution layout and the build, test and run commands. Verify: every command in it runs successfully as written.
- [ ] 4.2 Update `README.md` with prerequisites and the macOS Accessibility and Microphone permission notes. Verify: a reader can go from clone to running app using only the README.
