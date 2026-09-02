## Context

This application is tray-only. It has no window, no taskbar button and, in a release build, no
console — so the only thing it can say before its tray icon exists is what reaches
`~/.pisum-whisper/logs/pisum-whisper.log`, and one of the failures below is the log itself not
existing. Issue #20 is the reported symptom: a settings file that is not valid JSON exits the process
with a byte-identical log file and nothing on screen.

`add-system-integration` (change 11) shipped the notification transport and recorded, in its *Risks*,
that the transport cannot reach any of this — a drawn window needs `StartWithClassicDesktopLifetime`
to be pumping a queue. It deferred the gap, as change 9 had already deferred
`HotkeyAvailability.Failed` to it. This change is that deferral coming due.

Seven conditions, and where each is discovered:

```
Program.Main
  |
  +-- BuildHost(args)
  |     +-- AddFileLogging(...)  ........... [1] log directory unusable      DEGRADED
  |     |     LogDirectory.TryCreate() returns a reason; a logger is
  |     |     built anyway, with no file sink behind it
  |     +-- builder.Build()  ............... [2] ValidateOnBuild fails       FATAL
  |
  +-- LoadSettings(host.Services)
  |     +-- store.Load() -> Read()  ........ [3] invalid JSON (issue #20)    FATAL
  |     +-- store.Load() -> Write()  ....... [4] file not writable           FATAL
  |
  +-- host.Start()
  |     +-- GlobalHotkeyService.StartAsync
  |            ............................. [5] NotGranted / Failed         DEGRADED
  |
  +-- StartWithClassicDesktopLifetime(args, ...)
        +-- new App(services) -> LoadIcon
        |      .............................. [6] tray asset missing         FATAL
        +-- OnFrameworkInitializationCompleted    <=== a dispatcher exists from here
        +-- run loop ......................... [7] permission revoked        DEGRADED
```

[4] is not in issue #20 and is not reachable by its suggested fix: `SettingsStore.Write`
(`SettingsStore.cs:231`) is a bare `File.WriteAllText` + `File.Move`, called from `Load()` on a first
launch, so an unwritable home directory throws `UnauthorizedAccessException` rather than
`SettingsException`.

The work lands in three capabilities. `startup-diagnostics` is new and owns both transports, the exit
code, and the bound on what a failure is allowed to say. `global-hotkey` and `file-logging` each gain
one thing, and in both cases it is existing state becoming observable rather than new behaviour: a
change in whether the binding is being observed, and the reason a log directory could not be created
surviving the moment it was discovered.

## Goals / Non-Goals

**Goals.** Every one of [1] to [7] reaches the user rather than only a log that may not exist. The
fatal ones name what failed and where to look. The process exits with a non-zero code rather than an
unhandled exception.

**Non-Goals.** No in-app error console, crash reporter or error history. No repair of a corrupt
settings file and no start on in-memory defaults — it holds API keys a settings window would then
overwrite. No fourth tray icon state (change 9 rejected it). No change to the Hotkey tab's banner.

## Decisions

### 1. Two transports, split by when the failure happens rather than by how bad it is

The obvious split is severity: fatal gets a dialog, degraded gets a notification. That is the right
answer, but not for the reason it looks like. What decides which transport is *reachable* is whether
a dispatcher is pumping, and the two splits coincide only by luck:

|              | no dispatcher yet                      | dispatcher pumping |
|--------------|----------------------------------------|--------------------|
| **fatal**    | [2] [3] [4] [6] -> native modal dialog  | (empty)            |
| **degraded** | [1] [5] -> deferred, see decision 6     | [7] -> toast       |

The empty cell is why severity works as a proxy: nothing fatal happens after startup, because
everything after startup is a dictation and `DictationOrchestrator` already catches it. The
bottom-left cell is the one that needs a decision, and it is **not** "call `Present` earlier".

### 2. `IFatalErrorReporter` is constructed, not registered

`Core/Diagnostics/IFatalErrorReporter.cs` declares it; `Platform/Diagnostics/NativeFatalErrorReporter.cs`
implements it with a static `Create()` returning the Windows or the macOS implementation, or a no-op
on anything else.

It is **not** registered in the container, and that breaks the pattern `AddTextOutput` +
`AddNativeOutput` and `AddNotifications` + `ToastPresenter` established in changes 7 and 11. The
reason is [2]: one of its four call sites is `builder.Build()` failing, so a reporter resolved from
the container is a reporter that does not exist exactly when it is needed. `Program` news it up on
its first line, before anything else can fail — the same shape as `AddFileLogging(out var logger)`
handing `Program` a logger before the container exists.

The interface is there for the platform switch and for testability, not for injection.

### 3. The native dialog, and it differs on both platforms

**Windows.** `MessageBoxW` from `user32.dll` through `[LibraryImport]` with
`StringMarshalling = StringMarshalling.Utf16`, `hWnd = IntPtr.Zero`, and
`MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST` (`0x00050010`). It pumps its own modal loop, so
it needs neither a window nor a message pump of ours — which is the whole point.
`[SupportedOSPlatform("windows")]` plus the `OperatingSystem.IsWindows()` guard in `Create()` clears
`CA1416`, exactly as `WindowsClipboard` already does. No package reference is needed.

**macOS.** `Process.Start("/usr/bin/osascript", ["-e", script])` followed by `WaitForExit()`, with

```
display dialog "<message>" with title "<title>" buttons {"OK"} default button "OK" with icon stop
```

Both `"` and `\` have to be escaped into the AppleScript string literal, and that escaping is the
implementation's job, not the caller's.

**This is the transport change 11 rejected, and the rejection does not carry.** Change 11 turned down
`osascript -e 'display notification'` on three grounds; for a dialog raised while the process is
already dying, only the first survives:

| Change 11's objection | Still true here |
|---|---|
| Attributes the dialog to Script Editor | **Yes** — accepted, see *Risks* |
| A second implementation where drawing one is none | No — with no dispatcher there is nothing to draw with |
| A `Process.Start` on the thread that owns the user's hotkey | No — every call site is the main thread, and there is no hotkey left to protect |

`NSAlert` through the Objective-C runtime — which `MacOsClipboard` already establishes as a technique
in this repository — is rejected below: it needs `NSApplication.sharedApplication` and a run loop,
neither of which exists before Avalonia is initialised, and [6] is the case where Avalonia is what
failed.

**`Report` never throws.** It runs while a failure is already being handled, so an exception out of
`MessageBoxW`, or a missing `/usr/bin/osascript`, is swallowed. Losing the dialog is bad; losing the
exit code and the log line behind it is worse.

### 4. `Program.Main` gets one catch, and the logger has to outlive the host

The guard cannot simply be wrapped around the existing body, because of a scoping trap:

```csharp
using var host = BuildHost(args);   // disposes at the end of the enclosing scope
```

`using var` inside a `try` releases at the end of that `try` block — **before** the matching `catch`
runs. `AddFileLogging` registers the logger with `services.AddSerilog(serilog, true)`
(`FileLoggingServiceCollectionExtensions.cs:80`), so the container owns it, and a catch placed outside
the `using` would log `Fatal` into a logger that has already been disposed and dropped its queue.

Ownership therefore moves: `AddSerilog(serilog, false)`, and `Program` disposes the logger itself.

```csharp
[STAThread]
public static int Main(string[] args)
{
    var reporter = NativeFatalErrorReporter.Create();
    ILogger? logger = null;
    IHost? host = null;

    try
    {
        host = BuildHost(args, out logger);
        LoadSettings(host.Services);
        host.Start();

        try
        {
            return BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
    }
    catch (Exception exception)
    {
        var (title, message) = StartupFailure.Describe(
            exception,
            Path.Combine(LogDirectory.DefaultPath(), LogDirectory.LogFileName));

        logger?.Fatal(exception, "Startup failed: {FailureTitle}", title);

        // Before the dialog, not after: the asynchronous sink drops its queue rather than draining it
        // if it is never disposed, and the dialog blocks until the user dismisses it.
        (logger as IDisposable)?.Dispose();
        logger = null;

        reporter.Report(title, message);
        return 1;
    }
    finally
    {
        host?.Dispose();
        (logger as IDisposable)?.Dispose();
    }
}
```

`logger = null` in the catch is what stops the `finally` disposing it a second time; with
`AddSerilog(..., false)` the host no longer disposes it at all, so there is exactly one owner and no
reliance on Serilog's disposal being idempotent.

**It returns rather than rethrows**, and `BuildHost`'s existing catch — which today logs `Fatal`,
disposes the logger and rethrows — is **deleted rather than amended**. Because the `out` parameter is
assigned before `builder.Build()` runs, the catch above already does those same three things for all
four fatal cases; keeping both would write two `Fatal` lines and dispose the logger twice. Letting the
exception leave `Main` instead would put the operating system's own crash dialog on top of ours.

### 5. `StartupFailure.Describe`, mirroring `DictationFailure.Describe`

`Core/Diagnostics/StartupFailure.cs`, an `internal static class` whose
`Describe(Exception exception, string? logFilePath)` returns `(string Title, string Message)` —
deliberately the same shape as `DictationFailure.Describe`, including its rule that **the title comes
from the exception's type and never from matching its message text**:

| Exception | Title | Message |
|---|---|---|
| `SettingsException` | `Settings Error` | the exception's message, which already names the file |
| `UnauthorizedAccessException`, `IOException` | `Settings Error` | that the file could not be written, and the path |
| anything else | `Startup Error` | "Pisum Whisper could not start." plus the log file path |

Every message ends by naming `logFilePath`, because the dialog is a pointer to the detail rather than
the detail. The path is passed in rather than computed, and it is worded as where the log *would* be:
`Path.Combine(LogDirectory.DefaultPath(), LogDirectory.LogFileName)` cannot fail, but [1] and a fatal
case can coincide, and then the file it names does not exist. `Core/Diagnostics` is a new folder;
`openspec/config.yaml` already names `Diagnostics` as one of `Core`'s areas.

### 6. The degraded half runs in `App`, and `ShowFirstLaunch` is the precedent

[1] and [5] are discovered before any dispatcher pumps, so they are **not** reported where they are
discovered. They are reported from `App.OnFrameworkInitializationCompleted`, beside the call that
already does exactly this:

```csharp
ShowFirstLaunch(_settings, _services.GetRequiredService<INotificationService>(), ShowSettings);
```

`IsFirstLaunch` is known in `Program.LoadSettings`, before Avalonia exists, and change 11 deliberately
deferred showing it until `App`. Its own remarks say why: *"It lives here rather than in
`Program.LoadSettings` [...] because it shows a window and `Program` runs before Avalonia exists."*
The two degraded startup conditions are the same shape and take the same route.

**Nothing is buffered, because there is nothing to buffer.** Both conditions are queryable state by
the time `App` runs: `host.Start()` has returned, so `Availability` is settled, and `LogDirectory` is
a registered singleton. A replay queue would store a value that can simply be asked for. This also
sidesteps change 11's still-open question about `Dispatcher.UIThread` being first touched from a
pooled thread — the call site here is the main thread, which is the case Avalonia 12 explicitly
supports.

`internal static void ReportStartupConditions(LogDirectory logs, IGlobalHotkeyService hotkeys, INotificationService notifications)`
— static, taking its collaborators, so it is assertable without constructing an `App`, for the same
reason `ShowFirstLaunch` is.

**Both are forced (`Notify`, not `NotifyInformation`).** A hotkey that does not work makes the whole
application inert, and a log that is not being written removes the only place the user could have
found that out. Neither is chatter, so neither respects `ShowTrayNotifications`.

**On macOS a first launch will show two notifications**, the welcome and "keys are not being
observed", because the Accessibility grant cannot be present the first time. That is the correct pair
— the welcome points at the settings window and the second says why the hotkey does nothing — and
`MaxConcurrent` is 3, so neither displaces the other.

### 7. `LogDirectory` retains the reason it already computes

`LogDirectory.TryCreate()` already returns the failure reason, and `AddFileLogging` already logs it to
a logger with no file sink behind it. Adding `public string? FailureReason { get; private set; }`,
assigned inside `TryCreate`, makes it readable later. The instance `TryCreate` is called on is the
instance registered by `services.AddSingleton(options.Directory)`, so the reason survives to `App`
with no extra registration.

### 8. `IGlobalHotkeyService.AvailabilityChanged`, seeded and then subscribed

[7] — withdrawn while running — has no mechanism at all today. `Availability` is a plain property
assigned from three places (`GlobalHotkeyService.cs:146`, `:175`, and `RecordUnavailable` at
`:407`/`:416`/`:425`) and nothing observes it; `RecordUnavailable` runs from `RunHookAsync`'s catch,
which faults whenever `_hook.RunAsync` ends, long after `StartAsync` returned.

Add `event EventHandler<HotkeyAvailability>? AvailabilityChanged;` to the interface, raised from one
private `SetAvailability` that replaces the three assignments and raises only on an actual change.

**None of the three sites is the hook thread** — two are `StartAsync` or its continuation, the third
is `RunHookAsync`'s catch — and that has to stay true. `Core/Hotkeys/`'s standing rule is that nothing
but matching runs on the hook thread; a handler here reaches `INotificationService`, and moving any of
the three onto that thread would break the rule invisibly.

`App` **subscribes first and reads second**, keeping the last reported value so the seed does not
double-report:

```
subscribe -> read Availability -> report if != Available and != last reported
```

Subscribing before reading is what stops a transition landing between the two from being lost. It is
the same reasoning as the tray icon's seeded `ApplyState`, which exists because "a hotkey pressed
during Avalonia's platform initialisation opens a recording this icon would otherwise misreport".

### 9. Testing

| What | Where | Category |
|---|---|---|
| `StartupFailure.Describe` maps each type to its title | `Core.Tests/Diagnostics/StartupFailureTests.cs` | `Unit` |
| `LogDirectory.FailureReason` is set, and null when creation succeeds | `Core.Tests/Logging/` | `Integration` (temp directories) |
| `AvailabilityChanged` fires once per change, not per assignment | `Core.Tests/Hotkeys/GlobalHotkeyServiceTests.cs` | `Integration` (its base creates a temp home) |
| `ReportStartupConditions` notifies per degraded state, and is silent when both are healthy | `App.Tests/StartupConditionsTests.cs`, the shape of `FirstLaunchTests` | `Unit` |
| The seed does not double-report when the event also fires | `App.Tests/StartupConditionsTests.cs` | `Unit` |
| A dialog actually appears, on each platform | manual task | `Manual` |

The reporter's two native implementations are not unit-testable — a modal dialog is a person looking
at a screen — so they are verified by manual tasks, in the shape of `ManualClipboardRoundTrip`.
`Program.Main` is likewise verified by hand: it is a static entry point, and the guard's value is that
the process does not vanish, which no headless test observes.

### Rejected alternatives

- **Call `INotificationService` where [1] and [5] are discovered.** No dispatcher is pumping there, so
  the job is enqueued into a loop that has not started; and for [5] the call would arrive on a pooled
  thread, which is exactly change 11's unresolved hazard of the first `Dispatcher` binding `_thread`
  to a thread that then goes back to the pool.
- **A buffered `StartupDiagnostics` collector drained by `App`.** It works, but it stores what can be
  read: both conditions are still true and still queryable when `App` runs.
- **`NSAlert` through the Objective-C runtime.** Needs `NSApplication.sharedApplication` and a run
  loop; [6] is the case where Avalonia failed to give us either.
- **Start a minimal Avalonia application to show an error window.** Two of the four fatal cases are
  the container or Avalonia itself failing, and it costs a second application lifetime to report the
  first one dying.
- **An `AppDomain.UnhandledException` handler.** Fires too late to control the exit code, cannot
  deterministically drain the logger before the process goes, and catches far more than startup.
- **Write to stderr.** A `WinExe` launched from Explorer or from autostart has nowhere for stderr to
  go, which is the reason `add-file-logging` exists at all.
- **Register `IFatalErrorReporter` in the container.** One of its call sites is the container failing
  to build.
- **A fourth tray icon state for the degraded cases.** Rejected by change 9, and it would say nothing
  at the moment [5] is discovered, because the tray icon does not exist yet either.
- **Splitting this into two changes, one per transport.** The seam is real and unambiguous — the fatal
  half depends on nothing and closes issue #20 alone, the degraded half depends on change 11 — and it
  was weighed rather than overlooked. It is rejected on two counts. The four fatal cases and the two
  degraded ones are observed in the same launch cycle, so splitting turns one by-hand pass into two on
  each platform, against a verification queue that already carries twenty items across seven archived
  changes. And decision 1 only holds with both halves in view: the point that the transports are split
  by *when* a failure happens rather than by how bad it is cannot be made by either half alone, and
  each change would inherit half a table and none of the argument. **What the split was wanted for is
  available without it** — tasks 1.1 to 3.2 are ordered first, depend on nothing in sections 4 and 5,
  and close issue #20 on their own, so the fatal half can land as its own commit or pull request inside
  this one change.

## Risks / Trade-offs

**`SettingsException` embeds `JsonException.Message`, and the settings file holds API keys.**
Measured rather than assumed — see *Open Questions*, where this is settled: no reader template
captures a value, so the worst case is a short literal token, and the message is passed through. The
residual is narrow but real, and it is written down here rather than left in the code: a value whose
opening quote is missing parses as a JSON literal instead of a string, and a value beginning `t`, `f`
or `n` then returns a few of its leading characters inside `'{0}' is an invalid JSON literal.`
Two independent corruptions have to line up for that, and no template can return a whole value.

**The same message already reaches the log today**, so this change does not create the exposure — it
widens the surface from a file to a screen, which is exactly the argument change 11 made for giving
notifications their own disclosure rule. `SettingsStore.Read`'s existing log line inherits the same
verdict, and it is a pre-existing finding rather than one this change introduces.

**The macOS dialog is attributed to Script Editor.** A fatal error appearing to come from another
application is poor, and it is accepted because the alternative is nothing at all. It is also
temporary in principle: change 12 builds the `.app` bundle that would make a bundled helper or a
proper alert plausible.

**Nothing on macOS has been run.** The dialog, its attribution, whether it comes to the front for a
process configured `ShowInDock = false`, and both degraded notifications all need Apple Silicon. This
joins the single sitting changes 8, 9, 10 and 11 already owe, and it means this change must not be
archived as verified on the strength of its Windows half.

**A modal dialog at login.** Autostart launches this process during the login storm, so [3] and [4]
put a blocking dialog in front of a user who has not finished logging in. It is still the right
behaviour — the alternative is a dictation tool that silently is not running — but it is worse than it
sounds, because `MessageBoxW` blocks until dismissed and the process stays alive until it is.

**[6]'s audience is a developer, not a user.** `CLAUDE.md` describes the missing tray asset as the
forgotten `Template` export that "leaves Windows building and passing while macOS throws". Shipping a
dialog for it is right, but it shares a transport with genuine user-facing failures and will mostly be
seen during development.

**Five seconds of nothing remains, and this change does not fix it.** `host.Start()` blocks on
`GlobalHotkeyService`'s start timeout before Avalonia runs, so on the [5] path there is no tray icon
and no notification for five seconds — the exact window the *Why* is about. Shortening the timeout is
change 6's decision and is deliberately not reopened here.

**The `AddSerilog` ownership change is invisible until it is wrong.** Moving disposal from the
container to `Program` is behaviourally identical on the success path. If a later change puts
`dispose: true` back, the fatal path silently stops writing its `Fatal` line and no test fails.

## Open Questions

- ~~**Can `JsonException.Message` contain bytes from the settings file beyond the offending token?**~~
  **Answered: only a short literal token, never a value — so the message is passed through.** Read from
  the error templates in `System.Text.Json.dll` at `10.0.8`, the runtime `global.json` pins, which is
  stronger evidence than the source would have been because it is what actually ships.

  Every reader template a corrupt settings file can trip formats **one offending character** into
  `{0}` — `'{0}' is an invalid start of a property name.`, `'{0}' is invalid after a value.`,
  `'{0}' is invalid within a JSON string.`, `Invalid leading zero before '{0}'.` — and that character
  is the corruption rather than the content. The type-conversion template formats a *type name*. The
  one exception is the literal family, `'{0}' is an invalid JSON literal. Expected the literal 'true'.`
  and the `GetInvalidLiteralMultiSegment` helper beside it, which capture the token being read: `tru3`
  comes back whole. Reaching it with key material needs the value's opening quote gone **and** the
  value beginning `t`, `f` or `n`, and it returns only the literal's length.

  `JsonException.Path` — `$.providers[0].apiKey` — names which property failed and never its value.
  It is not a disclosure: the schema is in the repository already, and it is the single most useful
  thing for repairing the file by hand.

  Summarising to a line and position was the alternative and is rejected: it deletes exactly what
  issue #20 holds up as the actionable part of the report, in exchange for a handful of leading
  characters in a case that requires two corruptions to coincide.
- **Does `MessageBoxW` come to the foreground from a process with no window and no owner?**
  *Answered for an interactive launch, see Verification results:* foreground, topmost and uncovered.
  What remains is the launch the question was written about — from the `Run` key at login, where
  no foreground process has handed the right on. Tracked on issue #30 as the remainder of 7.3.
- **Does `osascript`'s dialog come to the front when the calling process is not in the dock?** It runs
  in Script Editor's context, which activates — but `ShowInDock = false` is set on Avalonia's options
  and Avalonia is not up at that point, so what this process looks like to the window server is not
  obvious. Part of the Apple Silicon pass.
- **Should [1] also degrade the settings window's Logging tab?** That tab shows the log directory path
  and offers "Open Log Folder", both of which mislead when the directory could not be created. Out of
  scope here by the banner non-goal's reasoning, but it is the same defect one surface over.

## Verification results

Run on 2026-09-02 on win-x64 (Windows 11 Pro 10.0.26200) against a Release build of `main` at
`aeb1e15`, started through `Start-Process` from an interactive session rather than from Explorer —
equivalent for the purpose here, since a Release build has no console sink and a `WinExe` started
that way is attached to no console. **Only the corrupt-settings reproduction of 7.1 was run**, the one
issue #20 describes; the other three reproductions and every macOS row are still open, so 7.1 and 7.2
stay unticked. The settings file was backed up by hash first and restored byte for byte afterwards.

| # | What was checked | Result |
|---|---|---|
| 7.1 | `~/.pisum-whisper.json` replaced with `{ "startWithSystem": true, "hotkey": { BROKEN` and the application launched | **PASS** — a `Settings Error` dialog 683 ms after launch, owned by the launched process, naming the file, `Path: $.hotkey`, `BytePositionInLine: 39` and the log path; dismissing it (by posting `WM_CLOSE`, which a `MB_OK` box answers as OK) exits with code 1; the log gains `[FTL] Startup failed: Settings Error` with the full `SettingsException` beneath it; the corrupt file's hash is unchanged after exit |
| 7.3 | Whether `MessageBoxW` comes to the foreground from a process with no window and no owner | **PASS for an interactive launch** — `GetForegroundWindow` returned the dialog, `WS_EX_TOPMOST` was set, and `WindowFromPoint` at its centre and both corners resolved to the dialog, so nothing covered it. The login-time launch was not observed |

**Issue #20 closes on the first row.** What it reported was a log file byte-identical before and after
the failure; the same reproduction now leaves the `Fatal` line and the exception behind it, drained
before the dialog is shown, and the process ends with a non-zero exit code instead of an unhandled
exception on a stderr nothing reads.

**What was disclosed is what 1.1 predicted.** The dialog and the log quote the one offending character
(`'B'`) and name the property (`$.hotkey`); no value from the file appears in either.

### The remaining three reproductions of 7.1, run 2026-09-02 (settle-win-x64-verification-debt, tasks 5.1–5.3)

Against a Release build of `change/settle-win-x64-verification-debt` at `c40a1f3` — no code differs
from `main` at that commit — driven by `spikes -- fatal <exe> <title>` (launch, wait for a matching
window, post `WM_CLOSE`, wait for exit, read the newest `[FTL]` line), added by task 5.1 and kept
under `spikes/` rather than thrown away, per this change's own task 7.1 standing decision.

| # | What was checked | Result |
|---|---|---|
| 7.1 | `~/.pisum-whisper.json` moved aside and replaced with a directory of the same name, then launched | **PASS** — a `Settings Error` dialog naming the path; dismissing it exits with code 1; the log gains `[FTL] Startup failed: Settings Error` with `System.UnauthorizedAccessException: Access to the path is denied.` at `SettingsStore.Write`'s `File.Move`. **Answers open question 8: it is `UnauthorizedAccessException`, not `IOException`.** The directory was removed, the leftover `.tmp` (present) was removed, and the file was restored and re-hashed to match the 1.2 backup |
| 7.1 | `AddNativeOutput()` commented out in `Program.BuildHost`, built Release, launched | **PASS** — a `Startup Error` dialog ("Pisum Whisper could not start."); dismissing it exits with code 1; the log gains `[FTL] Startup failed: Startup Error` with a `System.AggregateException` from `ValidateOnBuild` naming `Pisum.Whisper.Core.Output.ISystemClipboard` as the type that could not be resolved, for both `ITextOutput` and `DictationOrchestrator`. Reverted with `git checkout`; Release rebuilt from the clean tree before the next reproduction |
| 7.1 | `src/Pisum.Whisper.App/Assets/tray-idle.png` moved out of the tree, built Release, launched | **FAIL of the prediction, not of the application.** No `Startup Error` dialog appeared — the driver's 15 s wait for that title timed out. The log shows the crash happened exactly as predicted, `System.IO.FileNotFoundException: The resource avares://Pisum.Whisper.App/Assets/tray-idle.png could not be found.` at `App.LoadIcon` in the `App` constructor — but **`StartupFailure.Describe` mislabels it**: `FileNotFoundException` is an `IOException`, so it matches the `UnauthorizedAccessException or IOException` arm meant for `SettingsStore.Write`, and the dialog shown is actually titled **`Settings Error`** reading "The settings file could not be written: The resource avares://Pisum.Whisper.App/Assets/tray-idle.png could not be found." — a wrong, misleading message for a failure that has nothing to do with the settings file. This is a genuine defect in `StartupFailure.Describe`'s exception matching, not limited to this reproduction: **any** `IOException` raised during startup for a reason other than writing the settings file would be mislabeled the same way. Recorded here rather than fixed — out of this change's scope, which is verification plus the one gate `Present` needed. Tracked as issue #34. The file was moved back and Release rebuilt from the clean tree; `git status` was clean apart from the spike before and after |

**All four reproductions of 7.1 have now run.** Three passed exactly as predicted; the fourth
disclosed a labelling defect in `StartupFailure.Describe` rather than failing to crash. Per this
change's Decision 1, a box is ticked when the check ran, not when it passed — 7.1 is ticked with this
row recording the discrepancy. `report-startup-failures` task 7.1's own text said "rename a `tray-*.png`
in the build output"; that is corrected in `tasks.md` (settle-win-x64-verification-debt task 5.3),
since the build output holds no `.png` at all — `Assets\**` are `AvaloniaResource` items compiled into
`Pisum.Whisper.App.dll`, so the reproduction moves the source file instead.

**The login-time half of 7.3 and all of 7.2 remain open**, as does whether the dialog is foreground
when the process is started by the `Run` key at login rather than by something already holding the
foreground — the login-time half of 7.3 is task 5.4 of settle-win-x64-verification-debt; 7.2 needs
Apple Silicon and is tracked on issue #31.
