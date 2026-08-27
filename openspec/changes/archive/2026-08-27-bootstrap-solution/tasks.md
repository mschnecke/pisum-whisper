## 1. Spikes — Windows half (do these first — they gate everything else)

- [x] 1.1 Create a throwaway `spikes/` console project, outside the solution, referencing SharpHook 8.0.0. Verify: run on Windows, press and release Ctrl+Shift+Space while Notepad has focus, and confirm both a press and a release event are logged with the correct modifier mask.
- [x] 1.3a Extend the spike with `EventSimulator` sending Ctrl+V. Verify: put known text on the clipboard, focus Notepad, run the spike, and confirm the text is pasted.
- [ ] 1.5a **(blocked — 48 kHz path verified; no non-48 kHz endpoint exists on this machine, see `design.md`)** Spike SoundFlow: open a capture device at 48 kHz mono float32 and record 5 seconds to a raw file. Verify: sample count matches the duration, and it works on a device whose native rate is not 48 kHz (e.g. a 44.1 kHz input) — this is the check that decides whether the resampling stage stays deleted.
- [x] 1.6a Spike Avalonia 12.1 `TrayIcon`: show an icon, set a tooltip, swap the image on a timer, and add a two-item native menu. Verify: visible in the Windows notification area, tooltip shows, and the image swap is reflected.
- [x] 1.7 Spike Concentus 2.2.2 plus `Concentus.Oggfile` 1.0.7: encode the 1.5a recording to `.opus`. Verify: the file plays back in VLC or ffplay. Note that "does `Concentus.Oggfile` compile against Concentus 2.x" is already answered — it declares `Concentus >= 2.2.2` — so this spike checks header correctness (OpusHead, pre-skip, granule positions), not feasibility.
- [x] 1.9 Desk-research the macOS run-loop question from SharpHook and libuiohook documentation and source, since no hardware is available to answer it. Verify: `design.md` records the expected threading model, whether the hook needs the main thread, and what observation would falsify the expectation — all marked unconfirmed.
- [x] 1.8 Record each spike outcome in `design.md` under Open Questions and in the Platform verification matrix, marking each resolved, deferred, or replaced by its fallback. Verify: no Open Question remains unanswered **for win-x64**, and every deferred macOS row names the fallback it would trigger.

## 1b. Spikes — macOS half (BLOCKED — no Apple Silicon hardware)

Deferred, not abandoned. Each task below has a named fallback in the Platform verification matrix in
`design.md`, and this change stays open until they are run. The `spikes/` harness is kept in the
repository precisely so these are re-run rather than re-written.

- [ ] 1.2 Run the SharpHook spike on macOS. Verify: both edges logged; record whether the hook needs the main thread and whether a CFRunLoop is required, and confirm or falsify the expectation recorded by task 1.9. Note the Accessibility prompt behaviour.
- [ ] 1.3b Run the `EventSimulator` spike on macOS, sending Cmd+V. Verify: known clipboard text is pasted into a focused text editor.
- [ ] 1.4 Set up a stable development code-signing identity for macOS and sign the spike binary. Verify: grant Accessibility once, rebuild, rerun, and confirm the grant persists without re-prompting.
- [ ] 1.5b Run the SoundFlow capture spike on macOS. Verify: opens at 48 kHz mono float32 and delivers the expected sample count, including on a device whose native rate differs.
- [ ] 1.6b Run the `TrayIcon` spike on macOS. Verify: visible in the menu bar and the image swap is reflected; record whether the tooltip shows on `NSStatusItem` and whether template images are supported.

## 2. Solution skeleton

- [x] 2.1 Add `Directory.Build.props`: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`. Verify: `dotnet build` succeeds.
- [x] 2.2 Add `Directory.Packages.props` with `ManagePackageVersionsCentrally=true` and the versions pinned in the design. Verify: a project referencing a package without a version builds. This also requires a repository-local `NuGet.config`; see the Decisions entry in `design.md`.
- [x] 2.3 Create `src/Pisum.Whisper.Core` and add it to `Pisum.Whisper.slnx` via `dotnet sln Pisum.Whisper.slnx add`. Verify: `dotnet build` succeeds.
- [x] 2.4 Create `src/Pisum.Whisper.Platform`, referencing Core. Verify: builds.
- [x] 2.5 Create `src/Pisum.Whisper.App` (Avalonia 12.1), referencing Core and Platform. Verify: builds.
- [x] 2.6 Create `tests/Pisum.Whisper.Core.Tests` (MSTest, FakeItEasy, Shouldly) referencing Core, with one trivial passing test. Verify: `dotnet test` reports 1 passed.
- [x] 2.7 Confirm the whole solution builds for both target runtimes. Verify: `dotnet build src/Pisum.Whisper.App -r win-x64` and `-r osx-arm64` both succeed **from Windows** — cross-RID restore and build is the cheapest early warning that a dependency ships no `osx-arm64` native payload. Note that the RID must be given per project: `dotnet build -r <rid>` against `Pisum.Whisper.slnx` fails with NETSDK1134, which forbids building a solution for a specific runtime. Running the osx-arm64 output is task 1b.

## 3. Composition root

- [x] 3.1 Wire the generic host and `Microsoft.Extensions.DependencyInjection` in the App entry point. Verify: a debug log line at startup proves the container was built.
- [x] 3.2 Enable eager validation of the container (validate on build, validate scopes) so a bad registration fails at startup. Verify: temporarily register a service with an unsatisfiable dependency and confirm startup throws naming that service; then revert.
- [x] 3.3 Configure the Avalonia `AppBuilder` with `MacOSPlatformOptions { ShowInDock = false }` and add a stub tray icon with a Quit item. Verify: launch on Windows with no taskbar button and no console window. The macOS half — no Dock icon — is deferred with task 1b.
- [x] 3.4 Implement clean shutdown: stop hosted services and dispose native handles on Quit. Verify: quit and immediately relaunch twice with no device- or hook-in-use error, and confirm exit code 0.

## 4. Documentation

- [x] 4.1 Replace the placeholder "Status" section in `CLAUDE.md` with the real solution layout and the build, test and run commands. Verify: every command in it runs successfully as written.
- [x] 4.2 Update `README.md` with prerequisites and the macOS Accessibility and Microphone permission notes. Verify: a reader can go from clone to running app using only the README.
