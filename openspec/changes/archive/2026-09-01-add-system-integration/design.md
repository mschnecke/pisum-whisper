## Context

Two gaps remain between a working application and an installed desktop utility, and they are
unrelated to each other beyond both being system integration. The pipeline fails **silently** — change
8 wrote the five places a user is owed an explanation, gave each one a title and a message, and left
all five as log lines into a file nobody watches. And `AppSettings.StartWithSystem` has defaulted to
`true` since change 2 in a file no autostart code reads, so the settings window offers a switch that
records an intention and honours nothing.

Change 8 did not leave markers to fill in. It left the finished strings:

| Site | `DictationOrchestrator` | Title constant | Today |
|---|---|---|---|
| hotkey pressed while transcribing | `TryStartRecording` :358 | `InProgressTitle` | `LogInformation` |
| watchdog reached the ceiling | `ArmWatchdog` :590 | `AutoStoppedTitle` | `LogInformation` |
| capture would not start | `TryStartRecording` :381 | `DictationFailure.Describe` | `LogError` |
| the dictation failed | `RunAsync` :472 | `DictationFailure.Describe` | `LogError` |
| delivered, paste refused | `DictateAsync` :515 | `PasteFailedTitle` | `LogInformation` |

`DictationFailure.Describe` already produces all seven titles the proposal asks for — Recording,
Configuration, Network, Authentication, Rate Limit, Transcription and Output Error — by type and by
`ErrorCategory`, never by matching message text. Its own remarks name this change as the consumer that
would make it worth having. So the notification half of this change is a transport and a policy, and
touches the orchestrator only to add one call beside each of five existing log statements.

**The proposal has drifted in three places.** Each is corrected below rather than quietly ignored:

| The proposal says | What is true now |
|---|---|
| "Add `IShellService` to open the log folder (`explorer.exe` / `open`), completing the Logging tab." | Delivered by change 10 as `Core/Shell/ISystemShell` + `Platform/Shell/SystemShell`, wired into `LoggingViewModel` and registered by `AddNativeShell`. This change adds nothing. |
| Windows notifications via `CommunityToolkit.WinUI.Notifications`, needing an AUMID from a Start-menu shortcut | Not buildable here. The package's desktop half ships only under `lib/net5.0-windows10.0.18362`; a plain `net10.0` project resolves `lib/net5.0`, which carries `ToastContentBuilder` and no way to show anything. Adopting it means a `-windows` TFM, which this project has decided against. |
| macOS notifications via `osascript -e 'display notification'` | Rejected with the package, because the decision below is one implementation for both platforms. That line is from the reference's **installer script**, not its running app — `tauri-plugin-notification` uses the bundle identity — so it was always a downgrade rather than a port, and it attributes notifications to Script Editor. |

**Two spikes were written for this change and both are recorded in `spikes/Pisum.Whisper.Spikes`.**

`spikes -- notify` (S6) put `Shell_NotifyIcon` with `NIF_INFO` through three trials — a visible icon on
a message-only window, a hidden icon on the same, and a hidden icon on an unshown top-level window.
Every `Shell_NotifyIcon` call returned `TRUE`, the `NOTIFYICONDATAW` layout is the correct 976-byte v4
struct, and no AUMID was set. But **nothing reached the notification platform**: after three runs,
`%LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db` — copied with its `-wal`, or recent
writes are invisible — held no handler and no row for the spike process. Whatever those balloons do,
they do not persist in the Action Center. The same database does hold `net.pisum.transcript` as a
`NonImmersivePackage`, which is the reference's installer having registered its AUMID, and is direct
evidence for the packaging route this change declines to depend on.

`spikes -- toast` (S7) is the one that decided the change. It shows two application-drawn windows and
**measures** the result rather than asking, which is why it reaches a verdict unattended:

```
  screens 3 | primary 3840x2160 @1.5 | working area 3840x2088 (taskbar excluded)

  toast 1   hwnd 0xB091C    rect 3276,1920 540x144   visible   topmost-at-centre
  toast 2   hwnd 0x5C0BA8   rect 3276,1764 540x144   visible   topmost-at-centre

  foreground   before: rider64 (14952)
             after #1: rider64 (14952)   unchanged
             after #2: rider64 (14952)   unchanged

  Q0 on screen  PASS   Q1 focus never moved  PASS   Q2 inside working area  PASS
  Q3 they stack PASS   Q4 above other windows PASS      — all measured
```

Q0 exists because Q1 alone is a false pass: a window that never rendered also never takes focus. So
`IsWindowVisible` answers Q0, `GetWindowRect` against `Screen.WorkingArea` answers Q2, rect
intersection answers Q3, and `WindowFromPoint` walked to `GA_ROOT` answers Q4 — a click at each
toast's own centre lands on that toast, over a maximised Rider. Facts below marked *measured* come
from that run on win-x64; **nothing here has been run on macOS**, and what that leaves open is in
*Open Questions*.

Reference: `tray.rs` `send_notification` / `send_info_notification` for the policy,
`hotkey/manager.rs:205-330` for the call sites, `tauri-plugin-autostart` for startup, and the
`setup()` block of `lib.rs:583-597` for the first-launch sequence.

## Goals / Non-Goals

**Goals:**
- Every failure the pipeline already describes reaches the user without the log file being opened.
- Errors are shown whatever the user's preference; chatter respects it.
- A notification never moves focus, and never delays the hotkey.
- `StartWithSystem` becomes true of the machine rather than only of the settings file.
- One notification implementation serves both platforms.
- A new user is pointed at the API key field instead of a silent tray icon.

**Non-Goals:**
- No in-app notification centre, history, or transcript preview.
- No notification actions, buttons, or click-to-open.
- No `SMAppService` on macOS 13+; the LaunchAgent plist matches the reference and works further back.
- No packaging, no installer, no Start-menu shortcut, no AUMID (change 12).
- No change to `DictationFailure`, to the settings schema, or to any of the six title and message
  strings. This change makes the existing wording audible; it does not rewrite it.
- No respecting of Focus Assist or Do Not Disturb. See *Risks*.

## Decisions

### 1. The notification is a window this application draws

`INotificationPresenter` is implemented by an Avalonia window, not by a native notification API.

Every alternative costs something structural, and the two that survive scrutiny cost the two things
this project has explicitly decided not to spend. `CommunityToolkit.WinUI.Notifications` needs a
`-windows` TFM against `CLAUDE.md`'s "one `net10.0` target for every project" and a Start-menu shortcut
from change 12; `Shell_NotifyIcon` needs a second notification icon beside Avalonia's — Avalonia's own
is unreachable, `Avalonia.Win32.TrayIconImpl` being internal — and reaches no notification platform
anyway, so it does not buy the Action Center persistence that was its main advantage over drawing one.

Drawing it costs no package, no TFM, no AUMID, no `osascript`, and no dependency on change 12 **on
either platform**. It is the only option that is one implementation rather than a Windows/macOS pair,
and the only one that can be tested: `tests/Pisum.Whisper.App.Tests` already runs on
`Avalonia.Headless.XUnit`, so "a notification was shown, with this title, and dismissed" is assertable,
where no native transport can be checked by anything but a person looking at a screen.

The price is real and is paid in *Risks*: it ignores Do Not Disturb, it leaves no history, and the
appearance becomes ours to own.

### 2. Policy in `Core`, presentation in `App`, registered separately

```
  Core/Notifications/                          App/Notifications/
    INotificationService     <-- orchestrator     ToastPresenter : INotificationPresenter
    NotificationService                            ToastWindow
      reads ShowTrayNotifications
      -> INotificationPresenter  ------------->
```

Four types, mirroring `AddTextOutput` (Core) + `AddNativeOutput` (Platform) rather than inventing a
shape. `AddNotifications()` in `Core/Notifications/NotificationServiceCollectionExtensions.cs`
registers `INotificationService` -> `NotificationService`; `Program.cs` registers
`INotificationPresenter` -> `ToastPresenter` inline, beside `SettingsEditor` and
`SettingsWindowViewModel`. With `ValidateOnBuild` on, omitting the presenter is a startup failure
naming `INotificationPresenter` rather than a null reference at the first error a user hits.

`INotificationService` carries the two methods the reference has and no more:

```csharp
void Notify(string title, string message);             // forced; ignores the preference
void NotifyInformation(string title, string message);  // suppressed by ShowTrayNotifications
```

The split is not stylistic. Someone who silences status chatter still has to be told their API key is
rejected, and the reference encodes that as two functions over one `force` flag
(`tray.rs:76-105`). `NotificationService` reads `SettingsStore.Current.ShowTrayNotifications` **per
call**, matching `GeminiProviderPool` and `DictationOrchestrator`; there is no subscription to
`Changed` and nothing to rebuild.

The presentation half lands in `App` rather than `Platform`, which is a departure from the text-output
split and the precedent set by changes 9 and 10: the tray icon and the settings window are both
App-only with no type in `Core`, because they are Avalonia. `INotificationPresenter` is also the seam a
native transport would replace if change 12's packaging ever makes one worth having — that is what the
interface is for, and it is why the policy does not live behind it.

### 3. `Present` must not block, and this is a correctness rule

`INotificationPresenter.Present` posts to `Dispatcher.UIThread` and returns. It may not wait for the
window, and an implementation that does is wrong.

Two of the six call sites run on the hotkey dispatch loop. `GlobalHotkeyService` raises its events
synchronously from its channel read loop, so `TryStartRecording` — and therefore the "Transcription In
Progress" and capture-failure notifications — execute on that loop, where the very next item may be
the release edge that ends a recording. This is the same constraint that keeps `TextOutput` out of a
hook handler, applied one layer further out, and it is the constraint that quietly disqualifies
`osascript`: a `Process.Start` per notification is over a hundred milliseconds on the thread that
owns the user's hotkey.

Writing it down matters more than it looks, because the rule is invisible in the call site. A future
transport that blocks would pass every test in this change and break hold-to-record.

### 4. The six call sites, and which are forced

Five are one line each beside an existing log statement in `DictationOrchestrator`; the sixth is new
in `App`. The policy follows the reference function for function:

| Site | Call | Reference |
|---|---|---|
| Transcription in progress | `NotifyInformation` | `send_info_notification`, `manager.rs:205` |
| Recording auto-stopped | `NotifyInformation` | `send_info_notification`, `manager.rs:239` |
| Capture would not start | `Notify` | `send_notification`, `manager.rs:262` |
| Dictation failed | `Notify` | `send_notification`, `manager.rs:324` |
| Paste failed | `Notify` | `send_notification`, `manager.rs:316` |
| First-launch welcome | `Notify` | `send_notification`, `lib.rs:588` |

"Paste failed" is forced despite being logged at `Information` and despite not being a failure of the
dictation: the transcript is on the clipboard and nothing else will tell the user that a manual paste
is now theirs to do.

The orchestrator takes `INotificationService` as a seventh constructor dependency. `DictationFailure`
stays `internal` and unchanged — the orchestrator already calls `Describe` at both error sites and
holds the title and message in locals, so both lines are `_notifications.Notify(title, message);`
directly beneath the existing `_logger.LogError`.

**The shutdown filter stays ahead of the notification.** `RunAsync`'s
`catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)` already separates "the
user quit" from "the budget expired", and only the latter reaches `Describe`. Quitting must stay
silent; a toast posted into a dispatcher that is about to stop would never render anyway.

### 5. The toast itself

`App/Notifications/ToastWindow.axaml`, following the settings window's axaml-plus-code-behind layout
rather than the spike's code-only construction.

*Measured by `spikes -- toast`* unless noted:

- `ShowActivated = false`, `Topmost = true`, `ShowInTaskbar = false`,
  `WindowDecorations = WindowDecorations.None`, `CanResize = false`. `SystemDecorations` is obsolete
  in 12.1 in favour of `WindowDecorations`.
- 360 x 96 logical. At the 1.5 scaling of the spike machine that is 540 x 144 physical, and
  `GetWindowRect` returned exactly the requested rectangle, so the DPI arithmetic is right.
- Placed from `Screen.WorkingArea` and `Screen.Scaling` off `Screens.Primary`, 16 logical from both
  edges, stacked 8 apart. `Window.Position` is physical pixels while `Width` and `Height` are not,
  which is the whole reason `Scaling` is read.
- **The corner differs by platform and this does not pretend otherwise.** Windows notifications rise
  from the bottom right above the taskbar; macOS ones descend from the top right below the menu bar.
  `WorkingArea` is what keeps both clear of the taskbar, the Dock and the menu bar.
- Three at once, at most. A fourth closes the oldest. Unbounded stacking marches off the screen.
- Dismissed after 6 s by a `DispatcherTimer`, with the dwell injected as a `TimeSpan` so no test waits
  six seconds — the precedent is `SettingsEditor`'s injected debounce and `GeminiProvider`'s injected
  backoff.
- **No click handling.** Dismissal is by timer alone. On macOS clicking a non-key window would make it
  key and activate an accessory application, spending exactly the focus this whole decision protects.

**Fixed appearance, not a theme.** Explicit brushes, a dark card with light text, and no `FluentTheme`
dependency. `App.cs` records that the settings window pins `ThemeVariant.Light` deliberately, because
a dark theme is an untested non-goal; a notification overlay reads as a dark card on both platforms by
convention, so declaring its appearance outright sidesteps the question rather than reopening it.

`ToastPresenter` owns the live list, assigns slots, and is closed from `App.OnExit` beside
`_trayIcon?.Dispose()`, so an open toast cannot outlive the dispatcher that owns it.

### 6. Autostart is reconciled at startup, not toggled at the switch

`AutostartReconciler` is an `IHostedService` in `Core/Autostart/`. It reconciles once in `StartAsync`
and again on every `SettingsStore.Changed`: read `IAutostartService.IsEnabled()`, compare to
`Current.StartWithSystem`, and write only on a mismatch.

The obvious alternative is to call `Enable`/`Disable` from `GeneralViewModel.OnStartWithSystemChanged`.
It is the same amount of code and covers less. Reconciling covers the first-launch enable, the toggle,
a settings file edited by hand, and a Run value some other tool removed, through one path with one
test; the view-model version needs a second path for first launch and silently diverges the other two.
It is also cheap: `Changed` fires once per debounced commit, not once per keystroke, so this is one
registry read per save.

Reading before writing is deliberate and is the fix for a mistake already in the tree.
`GlobalHotkeyService.OnSettingsChanged` logs `"Hotkey rebound to {Binding}."` at `Information` outside
`HotkeyMatcher.Rebind`'s early return, so changing the audio format writes a misleading line at the
default level. A reconciler that logged unconditionally would reproduce it in a place where the write
is a registry mutation rather than a no-op.

**This is a deliberate divergence.** The reference enables autostart on first launch only
(`lib.rs:583`). Self-healing on every launch is the behaviour the setting actually claims.

### 7. Both autostart locations are injected, and that is what makes both testable

`Platform/Autostart/`, following `Platform/Output/`'s Windows/macOS pair and
`NativeOutputServiceCollectionExtensions`' `OperatingSystem.IsWindows()` / `IsMacOS()` /
`throw new PlatformNotSupportedException` shape, as `AddNativeAutostart()`.

| | Windows | macOS |
|---|---|---|
| Type | `WindowsAutostart`, `[SupportedOSPlatform("windows")]` | `MacOsAutostart`, `[SupportedOSPlatform("macos")]` |
| Location | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | `~/Library/LaunchAgents` |
| Identity | value name `Pisum Whisper` | `net.pisum.whisper.plist`, `Label` `net.pisum.whisper` |
| Payload | `"<exe path>"`, quoted | `ProgramArguments` `[<exe path>]`, `RunAtLoad` `true` |
| Injected as | the subkey path | the directory |

**`Microsoft.Win32.Registry` needs no package reference.** Verified in a scratch `net10.0` project with
`TreatWarningsAsErrors`: the types resolve from the shared framework, and the only diagnostics are
three `CA1416`s, cleared by `[SupportedOSPlatform("windows")]` on the type plus the
`OperatingSystem.IsWindows()` guard the registration extension already has to write. That is precisely
the pattern `WindowsClipboard` uses, so autostart adds no new mechanism and no new dependency —
`Directory.Packages.props` is untouched by this change.

Injecting the location is what makes the Windows half testable at all. Without it the only honest test
writes to the real `Run` key, which means a manual test gated on `PISUM_WHISPER_RUN_MANUAL` and a
capability verified by hand for ever. With it, a test writes to a private `HKCU` subkey and deletes it
in `Dispose`. The macOS half is symmetric and better off still: a plist written into a temp directory
is a real round trip **on any operating system**, so `MacOsAutostart`'s file format is covered from
Windows, and only its effect on login needs hardware.

No `launchctl`. `tauri-plugin-autostart`'s `MacosLauncher::LaunchAgent` writes the plist and nothing
else; `launchd` reads `~/Library/LaunchAgents` at login.

Failures raise `AutostartException`, mirroring `SystemShellException`. The reconciler catches and logs
it: a machine that refuses a Run value must not stop the application from starting.

### 8. First launch runs in `App`, not in `Program`

`SettingsStore.IsFirstLaunch` is known in `Program.LoadSettings`, before Avalonia exists, and the flow
has to show a window — so it belongs in `App.OnFrameworkInitializationCompleted`, after the tray icon
is created and after `ShowSettings()` can work. Two of the proposal's three steps are already covered
by decisions above: the autostart enable is the reconciler's `StartAsync`, which has run by then, and
the welcome notification is `Notify`. What is left here is `ShowSettings()`, one call.

Order matters slightly: the welcome is posted before the window is shown, so the notification does not
land on top of the thing it is pointing at.

### 9. Testing

| What | Where | Trait |
|---|---|---|
| `NotificationService` forces errors and suppresses info | `Core.Tests/Notifications` | Integration — `SettingsStore` needs a real file to hold a non-default flag |
| the five orchestrator sites notify, with the right titles | `Core.Tests/Dictation` | Integration — `DictationTestBase` |
| shutdown notifies nothing | `Core.Tests/Dictation` | Integration |
| `AutostartReconciler` writes only on a mismatch | `Core.Tests/Autostart` | Unit — fake `IAutostartService`, fake store |
| `ToastWindow` shows, stacks, caps at three, dismisses | `App.Tests/Notifications` | `[AvaloniaFact]` |
| `ToastPresenter` posts and does not block | `App.Tests/Notifications` | `[AvaloniaFact]` |
| `WindowsAutostart` round trip | `Platform.Tests/Autostart`, private `HKCU` subkey | Integration |
| `MacOsAutostart` round trip | `Platform.Tests/Autostart`, temp directory | Integration |
| registration resolves both halves | `Platform.Tests`, `App.Tests` | Integration |

**The trait rule needs one word.** `CLAUDE.md` defines `Integration` as creating a real file or
directory under the temp path, or building a real DI container or `Host`. A test that writes to the
real registry is neither, and is plainly not `Unit`. The rule gains "or writes to the real registry",
and `WindowsAutostartTests` is the one class that needs it.

### Rejected alternatives

- **`CommunityToolkit.WinUI.Notifications`** — its desktop half is absent from the `net5.0` asset a
  `net10.0` project resolves, so it needs a `-windows` TFM the project has decided against, plus a
  Start-menu shortcut from change 12. Last published November 2021.
- **`Microsoft.Windows.AppNotifications` (Windows App SDK)** — registers its own AUMID so it needs no
  shortcut, but still needs the `-windows` TFM and adds a WinAppSDK runtime dependency to a tray
  utility.
- **`Shell_NotifyIcon` with `NIF_INFO`** — no TFM change and no AUMID, but needs a second notification
  icon because Avalonia's is unreachable, and S6 showed it reaches no notification platform, so it
  does not deliver the Action Center persistence that was the reason to prefer it.
- **Hand-rolled WinRT COM to `ToastNotificationManager`** — no TFM change, but ~150 lines of interop
  and still an AUMID, so still change 12.
- **`osascript -e 'display notification'` on macOS** — identity-free and genuinely works today, but
  attributes every notification to Script Editor, can be silently blocked until the user allows that
  app, is a `Process.Start` on the hotkey dispatch loop, and would make notifications the one
  capability with two implementations instead of none.
- **Autostart driven from `GeneralViewModel`** — same code, covers less: a separate first-launch path,
  and no reconciliation of a file edited by hand or a Run value removed elsewhere.
- **Refusing to register autostart when the exe path is under `bin/`** — speculative complexity for a
  case a developer creates knowingly. See *Risks*.
- **A `force` flag on one `Notify` method** — one method with a boolean at six call sites reads worse
  than two named ones, and the reference already found the two-function shape.

## Risks / Trade-offs

**It ignores Do Not Disturb and Focus Assist, and that is the real cost of this decision.** A native
notification is suppressed while the user is presenting or in a game; a window this application draws
is not. It is topmost, so it will draw over a presentation. Nothing in this change mitigates it, and
nothing can without the native transports the change has just rejected — this is the trade accepted in
exchange for shipping notifications at all before change 12. The mitigations that exist are ordinary:
notifications are rare, five of the six are failures the user needs, and `ShowTrayNotifications`
already silences the two that are not.

**The transport cannot report a failure raised before the dispatcher loop starts, and that is the
second cost of this decision.** A native notification is posted from anywhere; a window this
application draws needs `StartWithClassicDesktopLifetime` to be pumping a queue, and the earliest
startup failures happen before it is ever called:

| Failure | When | Reportable by a toast |
|---|---|---|
| A corrupt settings file (issue #20) | `Program.LoadSettings`, before `host.Start()` | **No** — `Main` unwinds and the loop is never started, so a posted job is enqueued into nothing |
| `builder.Build()` fails `ValidateOnBuild` | before `host.Start()` | **No** — same, and this one is already logged `Fatal` |
| The hotkey grant is refused on macOS | `host.Start()` | Only if `Present` is safe from a pooled thread — see *Open Questions* |
| A tray asset is missing on macOS | the `App` constructor | **No** — `AssetLoader.Open` throws before the loop runs |
| Anything after initialisation | running | Yes |

This resolves a question issue #20 leaves open. That issue suggests surfacing the corrupt-settings
failure as a notification and says the decision "probably belongs with `add-system-integration`" — it
does, and the answer is that it cannot be done with this transport. Its fix is the log-`Fatal`,
dispose, rethrow it proposes and nothing more. **Startup failures being invisible to the user is
therefore a real gap that this change does not close**, alongside the `HotkeyAvailability.Failed`
case change 9 deferred here and this change deferred again; both want a proposal that is about
startup rather than about dictation.

**Nothing here has run on macOS.** Avalonia's own source says `ShowActivated: false` reaches
`[Window orderFront:Window]` with no `makeKeyAndOrderFront` and no `ActivateApplication`, which by
Apple's definition changes neither the key nor the main window — but that is reading, not running.
S7 falls back to three y/n prompts on macOS and is written to be re-run there unchanged. This is the
same Apple Silicon sitting that changes 8, 9 and 10 are already waiting on, and it should be done in
one pass with them.

**Z-order was measured against an ordinary maximised window**, not against another topmost window or
a full-screen exclusive application. A notification losing to a full-screen game is acceptable; the
measurement simply does not cover it.

**Autostart registers whatever `Environment.ProcessPath` is.** Under `dotnet run` that is
`bin/Debug/net10.0/Pisum.Whisper.App.exe`, and `StartWithSystem` defaults to `true`, so a developer's
first launch registers their build output for login. The reference behaves identically, the path is
logged at `Information`, and the alternative — a heuristic that refuses paths that look like build
output — is guesswork about a case the developer created deliberately. Before change 12 there is also
no `.app` bundle on macOS, so the LaunchAgent points at a raw apphost until packaging exists.

**A notification is a disclosure surface, and the five logging rules do not cover it.** `CLAUDE.md`
forbids logging transcript text, API key values, clipboard contents and non-hotkey keystrokes, and
every one of those arguments was made about the log file. A notification is more exposed, not less: it
is rendered on screen, over whatever the user is presenting. Nothing this change shows is derived from
a transcript — `TranscriptionException.Message` embeds up to 200 characters of Google's error body,
which `IsRetryable`'s status-before-body check guarantees is never a transcript, and the key travels
in a header rather than the query string — but the guarantee is currently written down only about
logging. `CLAUDE.md` should gain a sixth rule saying the same thing about notifications, and this
change is where that becomes true.

**The appearance is now ours.** Sizing to the text, wrapping a long provider error, stacking, and
dismissal timing are all work the operating system would have done. The spike proves the mechanism,
not that it looks good.

## Open Questions

- ~~**What happens when `Present` touches `Dispatcher.UIThread` from a pooled thread before Avalonia is
  initialised?**~~ **Answered by running it, see Verification results below (settle-win-x64-verification-debt, tasks 4.2 and 4.3): the process fails fast, exactly as the reading below predicts, and
  `ToastPresenter` now gates on it.** The orchestrator's hotkey handlers are armed at `host.Start()`, before
  `StartWithClassicDesktopLifetime`, and `App.cs` already acknowledges that window as real — its
  seeded `ApplyState` exists precisely because "a hotkey pressed during Avalonia's platform
  initialisation opens a recording this icon would otherwise misreport". A capture-start failure in
  that window calls `Present`, and `GlobalHotkeyService` raises its events from its channel dispatch
  loop, so that call arrives **on a pooled thread**.

  An earlier draft of this question predicted a late toast and called that correct behaviour. Read at
  the pinned `12.1.1` tag, that is the wrong thing to worry about. `Dispatcher`'s constructor sets
  `_thread = Thread.CurrentThread` — readonly, assigned once — then `s_uiThread ??= this` under the
  comment *"The first created dispatcher becomes 'UI thread one'"*, and when no platform
  implementation is available it falls back to `new ManagedDispatcherImpl(null)` **silently**, with no
  warning. `ReplaceImplementation` can swap the implementation in later, but it cannot change
  `_thread`, and it throws if `impl.CurrentThreadIsLoopThread` is false. So the risk is not a late
  notification: it is that the first `Dispatcher` is constructed on a thread-pool thread that has
  since gone back to the pool, and stays the process's `UIThread` for good.

  Avalonia 12's dispatcher rework (`AvaloniaUI/Avalonia#18586`, `#18686`) made it legal to use
  `Dispatcher.UIThread` **from `Main`** before initialising Avalonia. From `Main` — the main thread.
  The pooled-thread case is the one no guidance covers and the one this change creates.
  `ToastPresenter`'s *construction* is safe either way: it is built with the orchestrator at
  `host.Start()` and touches the dispatcher only inside `Present`.

  Task 7.4 settles it, and the check must post **from a pooled thread**; posting from the main thread
  exercises the case that already works and would return a false pass. If it does not hold,
  `ToastPresenter` drops notifications until `App` signals readiness — the first one is lost rather
  than the process, which is the same trade the table in *Risks* already accepts for everything
  earlier than `host.Start()`.
- **Does `ShowInTaskbar = false` also keep the toast out of the alt-tab list on Windows?** Exclusion
  needs `WS_EX_TOOLWINDOW`, and whether Avalonia sets it for a decorationless non-taskbar window was
  not measured. A toast in alt-tab is a blemish, not a defect; a glance during the S7 run settles it.
- **Does a toast draw over a full-screen macOS application, or does it need
  `NSWindowCollectionBehavior`?** Avalonia may not expose the behaviour, in which case notifications
  are simply not seen in full screen — acceptable, and worth knowing rather than discovering.
- ~~**Should `spikes -- notify` still be run?**~~ **Not pursued, deliberately — this is closed as a
  decision, not as an answer.** Its three observational questions are still unanswered and
  `Shell_NotifyIcon` may or may not draw anything on Windows 11. That was left unsettled because the
  option is rejected on grounds the spike had already established: it needs a second notification
  icon beside Avalonia's, and it reaches no notification platform, so even its best case is worse
  than the measured S7 result on every axis except Do Not Disturb.

  **Three attempts to answer it without a person failed, and the third is the one worth knowing
  about.** The notification database showed no handler and no row. Enumerating visible top-level
  windows before and during a run showed nothing new. That second result looked like an answer and is
  not one: a control run of `System.Windows.Forms.NotifyIcon.ShowBalloonTip` — an independent
  implementation of the same API — was **equally invisible** to the same detector, while an ordinary
  Notepad window was caught immediately. The instrument is blind to whatever a balloon is, so "no new
  window" measures the detector rather than the balloon.

  That control is the useful residue for change 12, when a Start-menu shortcut carrying an AUMID makes
  this live again. Run the WinForms balloon beside `spikes -- notify`: if the WinForms one appears and
  the spike's do not, the spike's struct or flags are wrong; if neither appears, Windows 11 does not
  surface these at all and the transport is dead regardless of the AUMID. Until then the spike stays
  in the tree unrun, and `CLAUDE.md`'s claim that the spikes harness is deletable is false while it
  does.

## Verification results

Run on 2026-09-02 on win-x64 (Windows 11 Pro 10.0.26200) under `dotnet run --project src/Pisum.Whisper.App`
(Debug), as part of settle-win-x64-verification-debt's tasks 4.2 and 4.3. **Only the dispatcher half
of 7.4 is recorded here; the alt-tab half stays open for task 4.1, so 7.4 stays unticked.**

| # | What was checked | Result |
|---|---|---|
| 4.2 | The throwaway edit from Decision 5 — `Task.Run(() => …Notify(…)).GetAwaiter().GetResult()` between `host.Start()` and `BuildAvaloniaApp` — run against the pre-gate `ToastPresenter` | **FAIL, matching the prediction exactly.** The process stayed alive with a window titled "Startup Error" (`MainWindowTitle` confirmed via `Get-Process`); the log carried `[FTL] Startup failed: Startup Error` followed by `System.InvalidOperationException: The calling thread cannot access this object because a different thread owns it.` at `Avalonia.Threading.Dispatcher.VerifyAccess()`, called from `InitializeUIThreadDispatcher` from `Win32Platform.Initialize`. No tray icon came up — the failure is inside Avalonia's own setup, before `App`'s constructor runs. Dismissed by posting `WM_CLOSE` to the dialog (the same mechanism `report-startup-failures` used), which exited the process; `Main`'s `catch` guarantees exit code 1 for this path, though the code itself was not independently re-observed after the dismiss (the `Process` handle obtained via `Get-Process` did not expose it) |
| 4.3 | The same edit, re-run against the gated `ToastPresenter` (this change's `Present` now asks `Dispatcher.FromThread(_uiThread)` before touching the dispatcher) | **PASS.** The process stayed alive with no window at all (tray-only, as expected); the log carried `[WRN] A notification was raised before the UI was ready and was dropped: Early` and no `Fatal` line; `Application started. Press Ctrl+C to shut down.` followed normally. `dotnet build Pisum.Whisper.slnx` at 0 warnings; `dotnet test tests/Pisum.Whisper.App.Tests` green at 167/167 including the two new gate tests |

Both throwaway edits were reverted with `git checkout -- src/Pisum.Whisper.App/Program.cs` immediately
after their run; `git status` was clean apart from the intended `ToastPresenter` and test changes
before either edit and after both. No transcript text, API key or clipboard content is implicated by
either run.
