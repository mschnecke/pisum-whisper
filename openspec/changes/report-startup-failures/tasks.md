## 1. Deciding what a fatal failure says

- [x] 1.1 **Answer the disclosure question before anything is written against it.** `SettingsException`
  wraps `JsonException.Message`, and `~/.pisum-whisper.json` holds API keys in plaintext. Issue #20's
  reproduction shows the message naming the offending token (`'B' is an invalid start of a property
  name`), so the question is whether a file corrupted *inside* a key value can put a fragment of that
  value into the message. **Answered from the error templates in `System.Text.Json.dll` at `10.0.8`,
  the runtime `global.json` pins — what ships rather than what the source says.** Every reader template
  formats one offending character into `{0}`; the type-conversion template formats a type name;
  `JsonException.Path` names the failing property and never its value. The one exception is the literal
  family (`'{0}' is an invalid JSON literal.`), which captures the token being read and needs a missing
  opening quote **and** a value beginning `t`, `f` or `n` to return a few leading characters. No
  template can return a whole value. **Decision: `Describe` passes the message through**, because
  summarising it deletes what issue #20 holds up as the actionable part. The existing
  `SettingsStore.Read` log line carries the identical message and so inherits the same verdict — a
  pre-existing finding, not one this change introduced. Verify: recorded in `design.md`'s *Open
  Questions*, struck through in the manner of change 9, with the residual case in *Risks*; 1.2 written
  to match.
- [ ] 1.2 Add `Core/Diagnostics/IFatalErrorReporter.cs` — one method, `void Report(string title, string message)`
  — and `Core/Diagnostics/StartupFailure.cs`, an `internal static class` whose
  `Describe(Exception exception, string? logFilePath)` returns `(string Title, string Message)`.
  Mirror `DictationFailure.Describe` exactly, **including its rule that the title comes from the
  exception's type and never from matching message text**:
  `SettingsException` and the `UnauthorizedAccessException`/`IOException` pair both give
  `Settings Error`, everything else gives `Startup Error`. Every message ends by naming
  `logFilePath` — passed in, not computed, and worded as where the log *would* be, since an unusable
  log directory and a fatal failure can coincide.
  `Core/Diagnostics` is a new folder and `openspec/config.yaml` already names `Diagnostics` as one of
  `Core`'s areas. Verify: unit tests in `Core.Tests/Diagnostics/StartupFailureTests.cs` covering each
  row of the table and the fallback. **Per 1.1 the parse message is passed through unchanged**, so add
  the disclosure test the `notifications` spec's analogous rule already has a precedent for: build a
  `SettingsException` from a genuine parse failure over a document holding a recognisable API key
  value, and assert the described message contains no part of that value. It guards the decision 1.1
  made rather than merely restating it. `Unit` trait — in-memory exceptions only.

## 2. The native dialog

**Nothing in this section may be exercised by an automated test.** `MessageBoxW` blocks until it is
dismissed, so a test that calls `Report` on Windows opens a modal dialog and hangs the suite — with no
timeout, because the run is waiting on a person. What is testable is the type `Create` returns and the
AppleScript escaping; the dialog itself is verified by hand in section 7.

- [ ] 2.1 Add `Platform/Diagnostics/WindowsFatalErrorReporter`, `[SupportedOSPlatform("windows")]`,
  calling `MessageBoxW` from `user32.dll` through `[LibraryImport]` with
  `StringMarshalling = StringMarshalling.Utf16`, `hWnd = IntPtr.Zero` and
  `MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST` (`0x00050010`). It pumps its own modal loop,
  which is why it needs neither a window nor a message pump of ours. The platform attribute plus the
  `OperatingSystem.IsWindows()` guard in 2.3 clears `CA1416`, exactly as `WindowsClipboard` already
  does — **add nothing to `Directory.Packages.props`**, the types are in the shared framework.
  `Report` catches and swallows: it runs while a failure is already being handled, and losing the exit
  code and the log line behind it is worse than losing the dialog. Verify: `dotnet build
  Pisum.Whisper.slnx` at 0 warnings, and by hand in 7.1 — see the note above for why there is no
  automated test here.
- [ ] 2.2 Add `Platform/Diagnostics/MacOsFatalErrorReporter`, `[SupportedOSPlatform("macos")]`, running
  `Process.Start("/usr/bin/osascript", ["-e", script])` and `WaitForExit()`, with
  `display dialog "<message>" with title "<title>" buttons {"OK"} default button "OK" with icon stop`.
  **Put the AppleScript escaping in a separate internal static function**, because it is the one part
  of the macOS half that can be verified from Windows: `"` and `\` both have to be escaped into the
  string literal, and a title or message carrying either — a Windows path in a message, a quoted file
  name — otherwise produces a syntax error and no dialog at all. Same swallow-everything rule as 2.1;
  a missing `/usr/bin/osascript` must not cost the exit code. Verify: unit tests over the escaping
  function alone — an embedded quote, an embedded backslash, both together, and a plain string passing
  through unchanged — which **run on Windows**, so only the dialog appearing needs hardware (7.2).
  `Unit` trait.
- [ ] 2.3 Add `NativeFatalErrorReporter.Create()` — `OperatingSystem.IsWindows()`, then `IsMacOS()`,
  then a **no-op reporter** rather than the `PlatformNotSupportedException` that
  `NativeOutputServiceCollectionExtensions` and `AddNativeAutostart` throw. This one deliberately
  diverges: it is the thing that reports startup failing, so it must not be a startup failure itself.
  It is **not registered in the container** and must not be — one of its four call sites is
  `builder.Build()` failing, so a reporter resolved from the container is a reporter that does not
  exist exactly when it is needed. Verify: a test resolving `Create()` and asserting the concrete type
  for the running platform, in the manner of `NativeShellRegistrationTests`; **and one asserting no
  `IFatalErrorReporter` is registered in a built host**, so a later change that "fixes" the omission
  fails a test that says why. `Unit` trait for the first, `Integration` for the second — it builds a
  container.

## 3. Guarding startup

- [ ] 3.1 Move ownership of the Serilog logger from the container to `Program`:
  `services.AddSerilog(serilog, false)` in `FileLoggingServiceCollectionExtensions.cs:80`, and
  `BuildHost(string[] args, out ILogger logger)` assigning the out parameter **immediately after
  `AddFileLogging`**, before anything that can throw. At runtime the caller's local is written even
  when `builder.Build()` throws afterwards, which is what makes one catch in `Main` able to log all
  four fatal cases. This is a change nothing observes on the success path, so cover it: a test that
  builds file logging over a temp directory, logs `Fatal`, disposes the logger and asserts the line
  reached the file. Without it, a later change restoring `dispose: true` silently stops the fatal path
  writing anything and no test fails. Verify: the new drain test, plus every existing test in
  `Core.Tests/Logging` still green (`dotnet test tests/Pisum.Whisper.Core.Tests --filter-namespace
  Pisum.Whisper.Core.Tests.Logging`). `Integration` trait — real files.
- [ ] 3.2 Restructure `Program.Main` around one `try`/`catch`, per `design.md`'s decision 4: construct
  the reporter first, hold `IHost? host` and `ILogger? logger` as locals, and in the catch describe,
  log `Fatal`, dispose the logger **before** the dialog so the asynchronous sink drains, null the local
  so the `finally` cannot dispose it twice, report, and `return 1`. **`using var host` must not go
  inside the `try`** — `using var` releases at the end of its own block, before the matching `catch`
  runs, which would hand the catch an already-disposed logger; `host` is disposed in the `finally`
  instead. **Delete `BuildHost`'s existing catch entirely** rather than leaving it to log and rethrow:
  with 3.1 in place `Main`'s catch does the same three things for all four fatal cases, and keeping
  both writes two `Fatal` lines and disposes the logger twice. Verify: `dotnet test
  Pisum.Whisper.slnx --filter-not-trait Category=Manual` green and `dotnet build` at 0 warnings; the
  behaviour itself by hand in 7.1, because `Main` is a static entry point and the guard's value is that
  the process does not vanish, which no headless test observes. **This closes issue #20.**

## 4. The degraded conditions become readable

- [ ] 4.1 Add `public string? FailureReason { get; private set; }` to `Core/Logging/LogDirectory.cs`,
  assigned inside `TryCreate` from the reason it already computes and currently returns and discards.
  The instance `AddFileLogging` calls `TryCreate` on is the instance it registers with
  `services.AddSingleton(options.Directory)`, so the reason survives to `App` with no new registration
  and no new service. Verify: tests asserting the reason is null after a successful create and
  non-null after a failed one — force the failure portably by pointing the directory at a path whose
  parent is an existing **file**, which makes `Directory.CreateDirectory` throw `IOException` on both
  platforms. `Integration` trait — temp files.
- [ ] 4.2 Add `event EventHandler<HotkeyAvailability>? AvailabilityChanged;` to `IGlobalHotkeyService`,
  raised from one private `SetAvailability` in `GlobalHotkeyService` that replaces the three current
  assignments (`:146`, `:175`, and `RecordUnavailable` at `:407`/`:416`/`:425`) and **raises only on an
  actual change**. **None of the three sites is the hook thread** — two are `StartAsync` or its
  continuation, the third is `RunHookAsync`'s catch — and that has to stay true, because a subscriber
  here reaches `INotificationService`; `Core/Hotkeys/`'s standing rule is that nothing but matching runs
  on the hook thread. Adding to the interface touches every implementation: `DictationTestDoubles.cs`
  and `ManualDictationSmokeTest` have fakes, and `HotkeyViewModel` consumes the interface. Verify:
  tests in `Core.Tests/Hotkeys/GlobalHotkeyServiceTests.cs` — the event fires once for a real
  transition, does not fire when the same value is assigned again, and carries the new value; plus the
  existing `OtherStartupFailure_IsReportedAsAFailureRatherThanAPermission` still passing.
  `Integration` trait — `GlobalHotkeyServiceTestBase` creates a temp home.

## 5. Reporting them

- [ ] 5.1 Add `internal static void ReportStartupConditions(LogDirectory logs, IGlobalHotkeyService hotkeys, INotificationService notifications)`
  to `App`, and call it from `OnFrameworkInitializationCompleted` beside the existing
  `ShowFirstLaunch(...)` call. Static and taking its collaborators, for the same reason
  `ShowFirstLaunch` is: constructing an `App` opens tray assets and registers a native tray icon, and a
  headless platform provides neither. **Subscribe to `AvailabilityChanged` first, then read
  `Availability`**, keeping the last reported value so the seed does not double-report — subscribing
  before reading is what stops a transition landing between the two from being lost, the same reasoning
  as the tray icon's seeded `ApplyState`. Report `logs.FailureReason` when it is non-null and
  `Availability` when it is anything but `Available`. **Both are forced (`Notify`, not
  `NotifyInformation`)**: a hotkey that does not work makes the application inert, and a log that is
  not being written removes the only place the user could have found that out. Nothing here is
  buffered or replayed, because both conditions are still true and still queryable by the time this
  runs. Verify: tests in `App.Tests/StartupConditionsTests.cs` — one notification per degraded
  condition, silence when both are healthy, no second notification when the event repeats a value
  already reported, one when it changes to a new one, and that both fire with the preference off.
  `FirstLaunchTests` is the shape to follow, but its `RecordingNotificationService` fake is a private
  nested class and needs extracting to be reused. `Unit` trait — fakes and a `LogDirectory` over a
  path nothing creates, so no Avalonia and no I/O.

## 6. Documentation

- [ ] 6.1 Update `CLAUDE.md`. Add a *Startup diagnostics* section beside the existing capability
  sections carrying (a) the two transports and that they are split by **when** a failure happens rather
  than by how bad it is; (b) that `IFatalErrorReporter` is constructed rather than registered, and why
  — one of its call sites is the container failing to build; (c) that `Program` owns the Serilog logger
  now, and that restoring `dispose: true` silently breaks the fatal path; (d) that `using var host`
  must stay out of the `try`; and (e) that a test calling `Report` on Windows hangs the suite on a
  modal dialog. **And correct the *Notifications and autostart* section's closing paragraph**, which
  says the startup gap and `HotkeyAvailability.Failed` "stay log-and-unwind" and want "its own
  proposal" — this change is that proposal, so the paragraph is false the moment this lands. Verify:
  all five are written down, the stale paragraph is gone, and `git show --stat` confirms the
  `CLAUDE.md` change lands with the code it describes rather than after it.
- [ ] 6.2 Update `openspec/ROADMAP.md`. Add `report-startup-failures` to the *Off-sequence changes*
  table with what it is and what it blocks (nothing — change 12 does not depend on it), since the
  roadmap's own standing decision is that off-sequence work gets a section rather than a number.
  Correct *Artifact status*, which lists changes 8 to 11 as owing verification but does not yet know
  about this change's own section 7. Verify: the section describes the tree as it stands, and
  `ls openspec/changes/` agrees with it.

## 7. Verification

- [ ] 7.1 Verify the fatal half on win-x64 by hand, following issue #20's own reproduction. For each of
  the four: corrupt `~/.pisum-whisper.json` and launch; make the settings file unwritable and launch
  with it absent so `Load` tries to create it; comment out `AddNativeOutput` to fail `ValidateOnBuild`;
  rename a `tray-*.png` in the build output. In every case confirm a dialog appears naming what
  failed, that dismissing it exits the process with code 1, and that the log file has gained a `Fatal`
  line — the last of which is the half issue #20 says is missing today. Do this against a **Release**
  build launched from Explorer, not `dotnet run`, because a debug build has a console and the whole
  point is the case where nothing does. Verify: manual; record the results in `design.md` under a
  *Verification results* heading in the manner of change 7.
- [ ] 7.2 Verify both halves on osx-arm64 by hand, plus the three things only macOS can answer: that
  the `osascript` dialog appears at all, that it comes to the front for a process that is not in the
  dock, and that revoking Accessibility while the application is running produces a notification rather
  than silence. Launch once with Accessibility ungranted and confirm the "keys are not being observed"
  notification appears **alongside** the first-launch welcome, which is the expected pair on a first
  macOS launch because the grant cannot be present yet. Verify: manual on Apple Silicon; record beside
  7.1. This needs the same sitting as changes 8, 9, 10 and 11's outstanding macOS tasks and should be
  done in one pass with them — **and until it is, this change must not be treated as verified on the
  strength of its Windows half.**
- [ ] 7.3 Answer the remaining open question that needs running rather than reading: does `MessageBoxW`
  come to the foreground from a process with no window and no owner? `MB_SETFOREGROUND` is specified
  to, but Windows 11's foreground-activation rules are permissive about refusing, and a dialog behind
  the active window is barely better than no dialog. A glance during 7.1 settles it. Verify: recorded
  in `design.md`'s *Open Questions*, struck through in the manner of change 9.

## Not in this change

Recorded so they are not mistaken for oversights, each with its owner:

- **The settings window's Logging tab when the log directory is unusable.** It shows the directory path
  and offers "Open Log Folder", both of which mislead when `TryCreate` failed. 4.1 makes the reason
  readable, so the fix is small — but it is the same defect one surface over and belongs to
  `settings-window`.
- **The Hotkey tab's banner being stale.** `HotkeyViewModel.Availability` and `UnavailableBanner` are
  computed getters that raise nothing, so the banner already does not update when access is revoked
  with the window open. 4.2's event makes fixing it a two-line change, and it is still
  `settings-window`'s to make. It is a **pre-existing** defect, not one this change introduces.
- **The five seconds before anything appears on the `[5]` path.** `host.Start()` blocks on
  `GlobalHotkeyService`'s start timeout before Avalonia runs, so a failed hook still shows nothing at
  all for five seconds — the exact window this change is about. Shortening or backgrounding that wait
  is `global-hotkey`'s decision and is deliberately not reopened here.
- **An environment variable to suppress the dialog for CI.** It would make 3.2 scriptable instead of
  manual, and it is speculative configuration nobody asked for. If a later change needs an unattended
  startup check, that is when to add it.
- **A single-instance guard.** Nothing stops two copies running, so nothing stops two dialogs. Out of
  scope and unrelated to this change; worth its own issue if it ever bites.
- **`HotkeyAvailability.Failed` in the tray icon.** Change 9 rejected a fourth icon state and this
  change does not revisit it — the icon does not exist yet at the moment the condition is discovered.
