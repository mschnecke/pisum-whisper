## Context

Every path through this product starts at one key combination. Change 8 cannot be designed until the
shape of that signal is fixed, and changes 9 and 10 both consume it — the tray icon reflects the
recording state the hotkey drives, and the settings window has to record a new binding using the same
hook. This change therefore fixes a seam, not just an implementation.

The reference's `hotkey/` module is three files. `parse.rs` (136 lines) and `conflict.rs` (97 lines)
port almost directly. `manager.rs` is 515 lines of which roughly sixty are hotkey handling; the rest
is the dictation pipeline and belongs to change 8. The port surface here is small, and most of the
work is in decisions the reference never had to make.

One correction to the proposal's framing, because it changes how much of the reference is
authoritative. The proposal says `RegisterHotKey` and `RegisterEventHotKey` are press-only, which is
true of those APIs, and concludes we need a raw hook. That conclusion stands. But the reference does
observe both edges today: the `global_hotkey` crate synthesises the release by polling
`GetAsyncKeyState` after `WM_HOTKEY`. So the reference's press/release state machine is a genuine
behavioural specification rather than something invented here — and our hook reports the release
natively rather than by polling, which is strictly better fidelity, not a new capability.

Change 1's S1 spike proved on win-x64 that SharpHook reports both edges with a correct modifier mask
while a foreign application has focus, and that the hook co-exists with Avalonia's run loop. The
macOS half is deferred on hardware. `HotkeyBinding` and `RecordingMode` already exist in
`Core/Settings/` from change 2, and `SettingsStore.Changed` already exists as the rebinding trigger.

Facts below marked *measured* were obtained on this win-x64 machine during design; facts marked
*from the assembly* were read out of SharpHook 8.0.0's shipped metadata and XML documentation.

This design was first written while every macOS question was deferred for lack of hardware. Issue #15
has since run change 1's spikes on an Apple M4, and the results are folded in below rather than left
as assumptions — two of them change decisions in this document. The macOS facts cited here come from
that run, recorded in `openspec/changes/archive/2026-08-27-bootstrap-solution/design.md`.

## Goals / Non-Goals

**Goals:**
- One observed key combination reporting both edges, system-wide, on Windows and macOS.
- A seam that change 8 can consume without inheriting the hook thread's real-time budget.
- A binding that changes at runtime without restarting the hook or the application.
- A hook that is safe to run in a keylogger-shaped component: it observes every keystroke on the
  machine and must never record one.

**Non-Goals:**
- The recording state machine — hold-versus-toggle interpretation, minimum duration, the maximum
  duration timer. Those are change 8, and this design deliberately keeps them out.
- The hotkey recorder UI, which is change 10. This change exposes the capture entry point it needs
  and stops there.
- Per-preset bindings, multiple simultaneous bindings, mouse buttons, media keys.
- Blocking a conflicting binding. The system table warns; it never refuses.

## Decisions

**The service reports raw chord edges. Recording mode stays in change 8.**
`IGlobalHotkeyService` in `Pisum.Whisper.Core.Hotkeys` exposes `event EventHandler? Pressed` and
`event EventHandler? Released`, and knows nothing about `RecordingMode`. The capability this change
adds is *"a key combination is observed system-wide, reporting both press and release"* — recording
is not in it, and the proposal's non-goals say so.

The split is not quite as clean as it looks, and the seam is where it is for a specific reason.
Key auto-repeat is a **hook** concern: *measured*, `GetOptionalFeatureSupport()` on this machine
returns `KeyAutoRepeat` among its supported features, which means holding the chord raises repeated
`KeyPressed` events. Coalescing those into exactly one `Pressed` per physical hold belongs here,
because it is an artefact of the hook rather than of recording. The reference's 200 ms
`TOGGLE_DEBOUNCE`, by contrast, guards against a human double-tapping a toggle; it is about the
recorder's state and belongs in change 8. These two look like one problem and are not, and putting
them both here would drag `RecordingMode` across the seam for no gain.
*Alternative rejected:* a mode-aware service raising `RecordingRequested` / `RecordingStopRequested`,
reading `RecordingMode` itself. It gives change 8 a simpler consumer, but makes the `global-hotkey`
capability depend on `settings-persistence` for semantics rather than just for the binding, and makes
the hotkey recorder in change 10 — which must observe edges with no recording implied — a special
case rather than the same thing in a different mode.

**The matched chord is suppressed, which fixes the hook implementation as `SimpleGlobalHook`.**
*From the assembly:* `HookEventArgs.SuppressEvent` is documented as working only when set
synchronously on the hook's own thread, and both `EventLoopGlobalHook` and `TaskPoolGlobalHook`
explicitly ignore it. *Measured:* `EventSuppression` is in the supported feature set on this machine.
So suppression and `SimpleGlobalHook` are the same decision.

Suppression is the right behaviour for two reasons. It is parity — `RegisterHotKey` consumes the
combination it registers, so the reference's users never saw the chord reach the focused window.
And the default binding is Ctrl+Shift+Space, which is Parameter Info in Rider and in Visual Studio;
without suppression a five-second dictation delivers five seconds of key-repeat to whatever has
focus. That is not a corner case, it is the primary workflow of the person building this.

Only the **main key** is suppressed, never a modifier. Applications track modifier state from the
key events they receive, and swallowing a Ctrl-up leaves them believing Ctrl is still held. Both
edges of the main key are suppressed, symmetrically: suppressing the down and passing the up is
worse than doing neither.
*Alternative rejected:* `EventLoopGlobalHook`, which is what SharpHook's own README recommends for
everything but the simplest cases and which would remove the dispatch loop below entirely. Rejected
only because it cannot suppress.

**Nothing but matching happens on the hook thread; edges cross to consumers over a channel.**
This is the load-bearing decision of the change. On Windows the hook callback is
`LowLevelKeyboardProc`, which Windows removes without warning or exception if it exceeds
`LowLevelHooksTimeout` — 1000 ms by default, and *measured*, 5000 ms on this machine, so the budget
is more forgiving than the 300 ms figure often quoted for older Windows. On macOS the equivalent is
worse: an unresponsive `CGEventTap` is disabled by the OS, which is what
`UioHookResult.ErrorAxApiRevoked` reports.

Change 8's press handler opens a microphone. SoundFlow initialising a capture device can plausibly
exceed either budget, so the seam cannot be a synchronous event raised on the hook thread.

```
hook thread  (libuiohook callback — a hard OS-enforced budget)
  |
  |-- KeyPressed / KeyReleased
  |-- match against the compiled chord      a few comparisons under an uncontended lock
  |-- e.SuppressEvent = matched             must be decided HERE, synchronously
  +-- _edges.Writer.TryWrite(edge)          returns immediately
                   |
                   v
dispatch thread  (single consumer, strictly ordered)
  +-- raise Pressed / Released  ---------->  change 8's recording state machine
```

`System.Threading.Channels.Channel<HotkeyEdge>`, unbounded, `SingleWriter = true`,
`SingleReader = true`. Unbounded is defensible here precisely because the producer is a human
holding a key: a few edges per second at worst. Bounded with `DropWrite` was considered and rejected
outright — dropping a `Released` leaves change 8 recording forever, which is the single worst failure
this component can cause.
*Alternative rejected:* `ThreadPool.UnsafeQueueUserWorkItem` per edge. Cheaper, but the thread pool
gives no ordering guarantee, and a `Released` overtaking its `Pressed` produces exactly the stuck
recording the channel exists to prevent.

**A held chord ends when the chord breaks, not only when the main key is released.**
While engaged, the service disengages on the release of the main key *or* on the release of any
modifier the binding requires. This is what the reference does — its polling loop watches every key
in the combination — and it avoids the alternative's failure mode: releasing Ctrl and Shift while
still holding Space would otherwise keep recording while a bare, unsuppressed Space repeats into the
focused application.
*Alternative rejected:* main key only. Simpler to state and to implement, but it converts a sloppy
release into a stuck recording plus stray input.

**Modifier matching compares normalised groups, not raw mask bits.**
*From the assembly*, `EventMask` is a `uint` flag enum in which `Ctrl = 0x22 = LeftCtrl | RightCtrl`,
and likewise `Shift = 0x11`, `Meta = 0x44`, `Alt = 0x88`. Two consequences that are easy to get
wrong and produce a hotkey that "sometimes doesn't work":

- `mask.HasFlag(EventMask.Ctrl)` is `true` only when **both** Ctrl keys are down. The side-agnostic
  test is `(mask & EventMask.Ctrl) != 0`.
- The mask also carries `NumLock` (0x2000), `CapsLock` (0x4000), `ScrollLock` (0x8000) and
  `Button1`–`Button5` (0x0100–0x1000). Any equality test against the raw mask fails whenever Caps
  Lock happens to be on or a mouse button happens to be held.

So the hot path folds the mask into a four-bit group set — Shift, Ctrl, Meta, Alt, each set if any of
its bits is present — and compares that for **equality** with the chord's group set. Equality rather
than superset: a binding of `F9` alone must not fire when the user presses Ctrl+F9 in another
application.

The compiled form is `HotkeyChord`, an immutable record holding the group set and a
`SharpHook.Data.KeyCode`, produced once when the binding is parsed.

The state machine over it is `HotkeyMatcher`, a separate type rather than logic inside
`GlobalHotkeyService`. Splitting it is what makes every rule above testable without a keyboard, a
hook or a platform: the matcher takes a key code, a mask and the simulated flag, and returns the edge
to report and whether to withhold the event.

It takes an uncontended lock rather than being lock-free, which is a correction to this design's
first draft. A rebind mutates the same engaged state the hook thread reads, so the lock-free version
would need the chord swap and the engaged flag to move together, and they cannot. Key events arrive
milliseconds apart and a rebind is a user action, so the lock is never contended, and an uncontended
`Monitor` acquisition is tens of nanoseconds against a budget of a second or more — five orders of
magnitude of headroom. Being clever here would buy nothing and risk a real race.

Suppression is tracked by *which key* was withheld rather than by a boolean. The main key's press
and release must be withheld together, and the two are separated by the whole dictation — during
which the binding may have been disengaged by a modifier release, or replaced by a rebind. A flag
loses that, and the focused application then receives a key-up it never saw go down, which is worse
than receiving both.

**Rebinding swaps the compiled chord; it does not restart the hook.**
The service subscribes to `SettingsStore.Changed`, re-parses `AppSettings.Hotkey`, and publishes the
new `HotkeyChord` with a `Volatile.Write`. The hook keeps running throughout. The reference
unregisters and re-registers, because `RegisterHotKey` gave it no choice; we have one, and taking it
removes a window in which no binding is active and removes the possibility of a re-registration
failing and leaving the application with none. If the old chord is engaged at the moment of the swap,
a `Released` is emitted first — the chord it belonged to no longer exists, and change 8 must not be
left mid-recording.

**An unparseable binding falls back to the default chord, logs a warning, and does not rewrite
settings.** `KeyCodeMap` is `TryParse`-shaped and never throws on the hot path. On an unknown key or
modifier the service logs at `Warning` naming the offending token, and binds Ctrl+Shift+Space
(Cmd+Shift+Space on macOS) instead. This follows `SettingsStore`'s established repair-and-log
posture, with one deliberate difference: the store owns every write to `~/.pisum-whisper.json`, so
this change repairs its own in-memory view and leaves the file alone.
*Alternative rejected:* refusing to bind, which is what the reference does — `register` returns
`Err` and the application has no hotkey. For a tray-only process with no window, that is a silent
total failure; the user has no way to discover why nothing happens.

**`KeyCodeMap` is bidirectional, and its reverse direction is what makes the coverage gap visible.**
Forward, it maps the reference's alias vocabulary to `SharpHook.Data.KeyCode` — case-insensitively,
including the reference's aliases (`ESC`/`ESCAPE`, `DEL`/`DELETE`, `PGUP`/`PAGEUP`, `UP`/`ARROWUP`,
`-`/`MINUS`, and so on) and the modifier aliases `ctrl`/`control`, `alt`, `shift`, and
`meta`/`super`/`win`/`cmd`/`command`. Reverse, it maps each `KeyCode` to exactly one canonical name,
which is what change 10's recorder needs in order to write a captured key back into settings.

Note that SharpHook's names differ from the reference's `Code` names throughout — `Vc1` not
`Digit1`, `VcOpenBracket` not `BracketLeft`, `VcEquals` not `Equal`, `VcNumPad1` not `Numpad1`,
`VcUp` not `ArrowUp` — so this is a re-expression against a different enum, not a transcription.

The forward table is ported verbatim per the proposal, which means it covers letters, digits,
F1–F12, arrows, punctuation and numpad, and **does not** cover keys SharpHook can report such as
`VcF13`–`VcF24`, `VcPrintScreen`, `VcPause` and `VcSection`. The reverse direction makes that a real
constraint rather than a latent one: change 10's recorder must reject a captured key with no
canonical name and say so, because a key it cannot name is a key it cannot persist. This is recorded
as a deliberate verbatim port with a known, cheap-to-close gap — adding rows costs nothing — and the
trigger to close it is change 10, not now. Inventing names for 150 key codes today would put a
vocabulary into the settings file that neither the reference nor any consumer has asked for.

**The subsystem lives in `Core/Hotkeys/`, and `Pisum.Whisper.Core` takes the SharpHook reference.**
Change 1 enumerated `Platform`'s scope as "autostart, notifications, opening a folder, a macOS
permission check", which contains no input handling, and change 3 set the precedent by putting
Serilog in `Core` on the reasoning that CLAUDE.md's "no platform or UI dependencies" rules out
platform-*specific* code, not cross-platform libraries with native payloads. SharpHook presents one
API on both targets, the same as SoundFlow will for `Core/Audio` in change 4. Registration follows
`AddFileLogging`: an `AddGlobalHotkey` extension on `IServiceCollection` in
`Core/Hotkeys/GlobalHotkeyServiceCollectionExtensions.cs`, called from `Program.cs`.
*Alternative rejected:* `Pisum.Whisper.Platform`. It would split one cohesive component across two
projects — the matcher and the key table have no platform surface at all — and would put the only
unit-testable part of the change in the project with no test project.

**`GlobalHotkeyService` is a singleton that is also the `IHostedService`.** It has a lifecycle, so
unlike change 3's arrangement there is no reason to separate the two. Registered as
`AddSingleton<GlobalHotkeyService>()`, with `IGlobalHotkeyService` and `AddHostedService` both
resolving that same instance. `StartAsync` calls
`IBasicGlobalHook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true)`; `StopAsync` calls
`Stop`; `Dispose` disposes the hook. Starting from the host rather than from Avalonia's
`OnFrameworkInitializationCompleted` — which is where change 1's `combined` spike started it — keeps
composition in the composition root, and costs nothing: the hook needs no UI, and *from the
assembly*, `RunAsync`'s `useBackgroundThread` gives it a thread whose run loop it owns.

`GlobalHookType.Keyboard` rather than `All`: on Windows that installs only `WH_KEYBOARD_LL` and no
mouse hook, and on macOS and Linux it filters mouse events out of a single tap. There is no reason
for a dictation tool to observe mouse movement, and not observing it is both cheaper and easier to
justify to a user reading the source.

**SharpHook's README warns that only one `IGlobalHook` may run per process**, since they share one
static native callback. The singleton registration is what enforces that, and it constrains change
10: the hotkey recorder reuses this instance through a capture entry point on
`IGlobalHotkeyService`, and must not construct a hook of its own.

**This component observes every keystroke on the machine, so it never logs one.** Change 3
established that transcript text and API key values are never logged. That rule extends here, and
more strictly: the key code of an event that is **not** the configured binding is never written to a
log at any level, including `Trace`. What may be logged is the configured binding, the edges of that
binding, counts, and outcomes. Stated as a decision rather than left to judgement, because a `Trace`
statement dumping `e.Data.KeyCode` is the obvious thing to write while debugging this component and
turns a dictation tool's log file into a keylog — one that change 10's "Open Log Folder" button then
puts a click away.

**Simulated events are ignored outright.** *From the assembly*, `HookEventArgs.IsEventSimulated`
distinguishes injected events, and change 1's spike already flagged this as the mechanism that keeps
change 7's synthetic Ctrl+V from being observed by this hook. The service returns early on any
simulated event before matching. The cost is that `EventSimulator`-driven tests cannot exercise the
matcher through the real hook — which is fine, because they should not: see testing, below.

**libuiohook's own diagnostics are routed into `ILogger` at warning level and above.**
`SharpHook.Logging.LogSource.RegisterOrGet(SharpHook.Data.LogLevel.Warn)` forwards to the injected
`ILogger<GlobalHotkeyService>`. The failure this component has to be able to explain is "the hotkey
silently stopped working", and libuiohook's own messages are the only evidence of several of its
causes. `Warn` rather than `Debug` or `Info` deliberately: libuiohook logs per-event at the lower
levels, which would defeat the no-keystroke-logging decision above.

**Tests drive a fake provider, not a real hook.** *Verified against nuget.org:* `SharpHook.Testing`
8.0.0 exists and matches the pinned `SharpHook` 8.0.0. `SimpleGlobalHook` accepts an
`IGlobalHookProvider` in its constructor, so the test provider lets `Core.Tests` post synthetic
events and assert on emitted edges — covering auto-repeat coalescing, the chord-break rule, the Caps
Lock and mouse-button masking, left/right modifier equivalence, and the rebind-while-engaged case,
with no real hook and no machine-wide side effects. `SharpHook.Testing` is added to
`Directory.Packages.props`, which change 1 did not pin because nothing referenced it yet.
*Alternative rejected:* a hand-rolled `IKeyboardEventSource` seam over SharpHook, faked with
FakeItEasy. It avoids a package, but it is an abstraction over an abstraction — `IGlobalHookProvider`
already is the seam, and the README says as much.

**macOS: the Accessibility grant is checked, its absence is non-fatal, and recovery requires a
relaunch.** *From the assembly:* `IAccessibilityProvider.IsAxApiEnabled(promptUserIfDisabled)`
returns `true` unconditionally on Windows and Linux, `PromptUserIfAxApiDisabled` defaults to `true`,
and `RunAsync` throws `HookException` carrying `UioHookResult.ErrorAxApiDisabled` when the grant is
missing. The decision is to leave the prompt at its default so the first `RunAsync` on a fresh
install raises the system dialogue — which is the only way the process gets into the Accessibility
list at all — and to catch the `HookException` as non-fatal, exactly as change 3 treats an unwritable
log directory. The application starts, logs at `Error` that the hotkey is unavailable and why, and
exposes that state for change 9's tray tooltip and change 11's notification to surface.

No retry loop. macOS does not reliably deliver a newly granted Accessibility permission to an
already-running process, so polling would mostly produce a prompt storm; the reference required a
relaunch and so do we. This costs the user one restart on first run, on macOS only.

`ErrorAxApiRevoked` — the grant withdrawn, or the tap disabled for being unresponsive — is a distinct
path: it arrives mid-session, `HookDisabled` fires, and it is logged at `Error` separately from
"never granted", because the remedy differs.

**A lost release is synthesised rather than waited for.** If the service is engaged when
`HookDisabled` fires, or when `StopAsync` or `Dispose` runs, it emits `Released` before tearing down.
Session lock, fast user switching and a revoked tap can all consume the physical key-up. Without
this, change 8's only backstop is its ten-minute maximum-duration timer, which is not a backstop, it
is a ten-minute bug.

**`ConflictDetector` ports the reference's table and its order-insensitive comparison, and warns
only.** Modifiers are normalised through the same alias table as parsing and compared as a group
set, which makes the comparison order-insensitive by construction rather than by sorting both sides
as the reference does. The key is resolved through `KeyCodeMap` and compared as a `KeyCode`, which is
one deliberate improvement on the reference: it compares lowercased text, so a user who binds `Del`
rather than `Delete` gets no warning. Resolving first costs nothing and closes that. A binding the
vocabulary cannot resolve never conflicts, since it cannot equal a shortcut whose every part is
known — this reproduces the reference's behaviour, where an unrecognised modifier survives
normalisation as itself and therefore matches nothing. Two fidelity notes carried across knowingly: the
reference lists `meta`+`tab` twice, once for Windows and once for macOS, which is harmless; and
several entries — Ctrl+Alt+Del and Win+L in particular — describe combinations Windows handles in the
kernel and never delivers to a low-level hook at all, so binding them fails silently rather than
conflicting. Warning about them is still correct, and the table stays as the reference wrote it.
The detector is a pure function with no dependencies, called by change 10's settings window; nothing
in this change's runtime path consults it.

## Risks / Trade-offs

- **A handler that blocks the hook thread gets the hook silently removed by Windows, or the tap
  disabled by macOS.** → The channel is the structural mitigation: the hook thread's only work is a
  branch and a `TryWrite`. The residual risk is that a later change adds "just one small thing" to
  the hook handler. Written into the decisions above as a rule, and worth carrying into CLAUDE.md
  alongside change 3's logging rules.

- **Suppression is measured as supported on this machine, but not yet observed end to end.**
  `GetOptionalFeatureSupport()` reporting `EventSuppression` says the platform can; it does not prove
  that a suppressed chord fails to reach a focused application. → Verified by a task that suppresses
  the chord and confirms a foreign application does not receive it, following the shape of S1b, which
  proved the converse for paste by using a sentinel.

- **Auto-repeat is confirmed as a supported feature, not as observed behaviour.** `EventSimulator`
  does not auto-repeat, so change 1's spike never exercised it. → Verified by a task with a human
  physically holding the key, which also closes the hardware scan-code route S1 left open.

- **The service is engaged when the machine sleeps, locks, or switches user.** → Covered by the
  synthesised release on `HookDisabled`, but whether every one of those paths actually raises
  `HookDisabled` is unproven on both platforms. If one does not, change 8's maximum-duration timer is
  the only remaining bound.

- **Suppressing the main key changes behaviour the reference had by accident.** `RegisterHotKey`
  consumed the chord; a raw hook does not, so this is a deliberate re-creation of an effect rather
  than an inherited one. If suppression misbehaves on macOS, passing the chord through is a one-line
  fallback that degrades gracefully — the hotkey still works, it just also reaches the focused
  application.

- **The macOS run-loop question is settled, and it settled in this design's favour.** Change 1's
  desk research predicted co-existence; issue #15's Apple M4 run confirmed it — the `combined` spike
  matched the Windows result in shape, 9 presses and 9 releases marshalled through
  `Dispatcher.UIThread.Post` with no contention. That was the highest-severity open risk in the
  project and it is closed. What remains unmeasured on macOS is narrower: suppression of the matched
  chord, and whether `HookDisabled` fires on a lock.

- **A missing Accessibility grant on macOS is silent, which invalidated this design's first
  answer to it.** The decision above assumed `RunAsync` would throw `ErrorAxApiDisabled` and that
  the default prompt would put the process into the Accessibility list. Issue #15 found neither:
  macOS never prompted, nothing was thrown, and libuiohook blocked indefinitely at zero CPU with no
  TCC activity at all. → `StartAsync` therefore races the hook against a five-second timeout as well
  as against its own failure, and reports the timeout with the macOS precondition named. Without
  that, a Mac with no grant would hang host startup for good — a worse failure than the reference's,
  and in a process with no window to explain itself from. The `HookException` paths are kept: they
  are what a *revoked* grant produces, and they are cheap.

- **The grant is given to the launching process, not to this binary, and does not survive a
  rebuild.** Issue #15 had to grant Accessibility to Rider — the terminal's parent — and found the
  first run after each `dotnet build` observing zero key events before reverting to working on the
  second. That is change 1's task 1.4 (a stable signing identity) rather than anything this change
  can fix, but it is the first thing to suspect when the hotkey appears dead on a Mac.

- **`Core` gains a native dependency.** SharpHook ships `runtimes/*/native/`, which change 1 already
  confirmed restores for `osx-arm64` from Windows. The architectural cost is that `Core` is no longer
  purely managed; the precedent and the reasoning are change 3's, and change 4 will do the same with
  SoundFlow.

## Platform verification

Following change 1's convention: a deferred row carries no evidence either way and is not a failure.

| What must be demonstrated | win-x64 | osx-arm64 |
|---|---|---|
| Chord matched on both edges with the correct modifier groups | **PASS** — S1, plus unit tests over `TestProvider` | **PASS** — S1 on an Apple M4 (#15) |
| Left/right modifiers equivalent; lock keys and mouse buttons ignored | **PASS** — unit tests | deferred |
| Consumers never run on the hook thread | **PASS** — unit test times a two-second handler | **PASS** — `combined` co-existence (#15) |
| Hook survives a rebind without restarting | **PASS** — unit test | deferred |
| A denied Accessibility grant is non-fatal, and distinguished from a revoked one | **PASS** — unit tests over `RunResult`; the grant itself is n/a on Windows | **PARTIAL** — #15 found it silent, not thrown; covered by the start timeout, unverified on hardware |
| No keystroke other than the binding reaches the log | **PASS** — unit test at `Verbose` over 50 keystrokes | deferred |
| Application starts and logs the observed binding | **PASS** — `Observing the hotkey Ctrl+Shift+Space.` | deferred |
| Suppressed chord does not reach a focused foreign application | **PASS** — physical keys, held over a text editor | deferred |
| A physically held chord produces exactly one `Pressed` | **PASS** — physical keys, 7.9 s hold | deferred |
| `HookDisabled` fires on lock / sleep / user switch | **not verified — deliberately**, see Open Questions | deferred |

Both physical rows were verified by hand, because neither can be driven synthetically:
`EventSimulator`-injected events are ignored by design — that is what keeps change 7's paste from
being observed — and it does not auto-repeat.

**Auto-repeat, measured.** A 7.9-second physical hold of Ctrl+Shift+Space produced exactly one
`Pressed` and one `Released`, 7,892 ms apart. This closes the last thing S1 left open — change 1
drove the hook with `EventSimulator`, so the hardware scan-code route was unproven — and it is also
the only evidence that the channel and the dispatch loop deliver edges in the running application
rather than only over `TestProvider`.

**Suppression, verified.** The held chord inserted nothing into a focused text editor, and a plain
Space still typed normally straight afterwards, so the pass cannot be explained by a dead hook.

The `osx-arm64` column is no longer uniformly deferred. Issue #15 ran change 1's spikes on an Apple
M4 (macOS 26.6.2) after this design was written, and the rows that column can now answer are answered
above. What it cannot answer is what the spikes did not exercise: this change's own suppression
behaviour and its handling of a lock. Those stay deferred, and honestly so — the spike proved the
hook, not the matcher built on it.

## Open Questions

- **Does `HookDisabled` actually fire on session lock, sleep and fast user switching?**
  **Deliberately left unverified.** The check requires locking a live session, and the decision was
  that it does not earn that during this change. Recorded here so it is not mistaken for an
  oversight: the synthesised-release mitigation is implemented and unit-tested against the three
  paths it can reach — `HookDisabled`, `StopAsync` and `Dispose` — but whether the operating system
  raises `HookDisabled` on a lock is an assumption, on both platforms.

  What is at stake if the assumption is wrong: a binding held at the moment the session locks stays
  engaged, and the recording it started runs until change 8's maximum-duration timer expires. That
  timer is a bound, not a backstop. **Change 8 must therefore not treat the synthesised release as
  covering session lock until this is measured**, and if it turns out none of those paths raises
  `HookDisabled`, the fix is a second mechanism here — most plausibly treating a `Pressed` with no
  intervening `Released` beyond a threshold as broken — rather than a workaround there.

- **Should the capture entry point for change 10 suppress everything while capturing?** A recorder
  that does not suppress lets the user's candidate chord fire whatever it normally does in the
  settings window. A recorder that suppresses everything is a keyboard trap that a crash leaves
  behind. This is genuinely change 10's decision; it is noted here because the entry point's shape is
  fixed by this change and should not foreclose either answer.
