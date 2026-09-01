## Context

Change 10 is the last change before the application is usable by anyone other than its author.
Everything it configures already exists and already re-applies itself; what is missing is a way to
type into it. The settings file at `~/.pisum-whisper.json` is currently the only editor.

The important thing to establish before designing anything is how much of this change is actually
new, because the proposal inherited wording from the reference that overstates it. Changes 3, 5, 6
and 9 each built their half of this window in advance, and said so in their own XML documentation:

| What the window drives | Already exists | Where |
|---|---|---|
| Hot-swap the log level | `FileLoggingHostedService.OnSettingsChanged` -> `LoggingLevelSwitch` | `Core/Logging/FileLoggingHostedService.cs:63` |
| Rebind the hotkey | `GlobalHotkeyService.OnSettingsChanged` -> `HotkeyMatcher.Rebind` | `Core/Hotkeys/GlobalHotkeyService.cs:356` |
| Refresh the tray tooltip | `App.OnSettingsChanged` -> `ApplyTooltip` | `App/App.cs:151` |
| Pick up new provider entries | `GeminiProviderPool` reads `SettingsStore.Current.Providers` per call | `Core/Transcription/GeminiProviderPool.cs:62` |
| Pick up recording mode and duration | `DictationOrchestrator` reads `SettingsStore.Current` per dictation | `Core/Dictation/DictationOrchestrator.cs:298,572` |
| List models, test a key | `IGeminiKeyProbe`, built "for the settings window" | `Core/Transcription/IGeminiKeyProbe.cs` |
| Record a new binding | `IGlobalHotkeyService.CaptureAsync`, built "for the settings window's recorder" | `Core/Hotkeys/IGlobalHotkeyService.cs:41` |
| Warn about a system collision | `ConflictDetector.ConflictsWithSystemHotkey` | `Core/Hotkeys/ConflictDetector.cs` |
| Show where the logs are | `LogDirectory`, "a registered service ... so the settings window can show the path" | `Core/Logging/LogDirectory.cs:8` |

The whole right-hand column is dead code today, because `SettingsStore.Save` has no runtime caller.
This change is the caller. It adds a window, six tab views, their view models, one edit-and-persist
helper, one shell call, and a third test project — and nothing in `Core` beyond two small seams
named in Decision 6 and Decision 10.

## Goals / Non-Goals

**Goals**

- Every field in `AppSettings` is editable without leaving the application.
- An edit is durable without the user pressing anything, and visible in the running application
  without a restart, except where the setting itself cannot apply until the next launch.
- An API key can be checked before a dictation depends on it.
- The window costs nothing when it is never opened, which in a tray utility is most launches.
- Closing the window does not stop the application.

**Non-Goals**

- No dark theme, no localization, no input device picker, no About dialog. (Proposal.)
- No autostart and no notification transport. The General tab persists both flags; change 11 acts on
  them.
- No settings-file watcher. `SettingsStore` is cache-authoritative by design and says so; a hand
  edit made while the application runs is still lost at the next save, and this change does not
  revisit that.
- No provider-pool rebuild. See Decision 1.

## Decisions

### 1. The change is the window. Nothing re-applies settings that does not already.

The proposal's original wording asked for the provider pool to be rebuilt on save. That is the
reference's `apply_settings` shape, where a global pool is copied out of settings because Rust has no
authoritative in-memory store to read from. This codebase has one, and `CLAUDE.md` records the
rejection in as many words: *"The pool is never rebuilt ... No rebuild step, no change subscription
and no lock — the only durable state is the round-robin cursor."*

Adding one here would be worse than redundant. `GeminiProviderPool.TranscribeAsync` snapshots the
enabled entries **once per call**, deliberately, so that a save landing mid-transcription cannot
change the set between fallback attempts. A rebuild triggered from `Changed` would reintroduce
exactly the race that snapshot exists to prevent.

So: the window calls `SettingsStore.Save` (or `SavePreset` / `DeletePreset` / `SetActivePreset`), and
every consumer that needs to know has already arranged to find out. The proposal is corrected to say
so.

### 2. Edits are applied to a clone, and written after 400 ms of quiet

Two problems with copying the reference literally, which wires an input's `oninput` straight to
`persistSettings`:

**A write per keystroke.** `SettingsStore.Write` is `Serialize` -> `File.WriteAllText(tmp)` ->
`File.Move(tmp, target, true)`. Typing a 39-character Gemini key would be 39 serializations and 78
file operations, on the UI thread, and would raise `Changed` 39 times — each one rebinding the hotkey
matcher and re-parsing the log level.

**A half-typed key visible to a dictation.** If the view models bound to `SettingsStore.Current`'s
object graph, every keystroke would be visible to `GeminiProviderPool` *before* any save, because it
reads `Current.Providers` at transcribe time. A dictation started while the user is halfway through
pasting a key would authenticate with the prefix.

The design is therefore:

```
  VM property setter
        |
        v
  SettingsEditor.Edit(s => s.AudioFormat = Wav)
        |
        +-- pending is null?  --> pending = store.CloneCurrent()
        |
        +-- edit(pending)
        |
        +-- restart the 400 ms timer
                       |
                       v   (quiet for 400 ms, or FlushAsync)
              store.Save(pending); pending = null
                       |
                       v
                 Changed --> log level, hotkey, tray tooltip
```

`SettingsEditor` lives in `App/Settings/`, holds the pending clone under a `Lock`, and exposes
exactly two methods: `void Edit(Action<AppSettings> edit)` and `Task FlushAsync()`.

- **The clone is taken at the start of each quiet window, not once per window lifetime.** A clone
  held across the whole session would silently revert anything written to `Current` by another route
  — which the Presets tab does on every command, see Decision 7.
- **`CloneCurrent` round-trips through `SettingsJsonContext.OnDisk`**: serialize `Current`,
  deserialize it back. It is a deep clone, it costs one small serialization at the start of a quiet
  window rather than one per keystroke, and it guarantees the draft is exactly what a save would
  write — a field the on-disk context cannot round-trip would be a defect the clone exposes rather
  than hides. It goes on `SettingsStore`, which already owns that context.
- **Flush points are the window hiding, the application exiting, and every Presets-tab command.**
  The worst case for a lost edit is a process killed inside a 400 ms quiet window; the flush on hide
  and on `desktop.Exit` removes the two ordinary ways to end a session inside one.
- **400 ms** is a typing pause, chosen so that continuous typing coalesces and a deliberate pause
  commits before the user can reach the window's close button. It is a constant in `SettingsEditor`,
  deliberately not a setting. It has no counterpart in the reference, which has no debounce at all.
- **The delay is injected**, `Func<TimeSpan, CancellationToken, Task>` defaulting to `Task.Delay`,
  following `GeminiProviderPool`'s retry delay and `DictationOrchestrator`'s injected clock. No test
  waits 400 ms of real time.

`Edit` is called on the UI thread; the commit runs on a pooled thread when the delay completes. That
is safe because the commit touches only `SettingsStore`, and the two `Changed` subscribers that could
care — the tray tooltip and the icon — already marshal through `Dispatcher.UIThread.Post`, because
change 9 anticipated exactly this thread and said so.

### 3. Where the code lives, and a third test project

```
src/Pisum.Whisper.App/Settings/
    SettingsWindow.axaml{,.cs}      the shell: title, size, tabs, hide-on-close
    SettingsEditor.cs               Decision 2
    Views/       ProvidersView, PresetsView, HotkeyView,
                 AudioView, LoggingView, GeneralView   (.axaml{,.cs})
    ViewModels/  SettingsWindowViewModel, ProvidersViewModel, ProviderEntryViewModel,
                 PresetsViewModel, PresetEntryViewModel, HotkeyViewModel,
                 AudioViewModel, LoggingViewModel, GeneralViewModel

tests/Pisum.Whisper.App.Tests/      new project, added to Pisum.Whisper.slnx under /tests/
```

**Nothing goes in `Core`.** `Core` is "domain + orchestration; no platform or UI dependencies", and
change 9 set the precedent directly above this one: the tray icon is wholly in `App.cs` with no type
in `Core` and no `ITrayPresenter`, because a mapping with one consumer does not need a seam. The same
reasoning covers the view models. The two exceptions are Decision 6 (`ISystemShell`) and Decision 10
(`GeminiDefaults`), which are contracts rather than presentation.

**Six views rather than one.** A single `.axaml` with six `TabItem`s would be around 500 lines with
two item templates inside it. Six `UserControl`s match the reference's own component split, and let
an `[AvaloniaFact]` construct one tab without standing up the window.

**The third test project** is what change 9's tasks.md deferred here, calling its absence "the
weakest part of the change". It references `Pisum.Whisper.App`, `xunit.v3`, `FakeItEasy`, `Shouldly`
and `Avalonia.Headless.XUnit`, and takes the same csproj shape as `Core.Tests` — `OutputType` `Exe`,
`UseMicrosoftTestingPlatformRunner`, its own `xunit.runner.json` and its own `Traits.cs`.
`Pisum.Whisper.App.csproj` gains `<InternalsVisibleTo Include="Pisum.Whisper.App.Tests"/>`, matching
`Core.csproj`.

### 4. The window itself

| Requirement | Avalonia 12.1 |
|---|---|
| 700x540 | `Width`, `Height` |
| minimum 540x400 | `MinWidth`, `MinHeight` |
| resizable | `CanResize = true` (default) |
| **not** maximizable | `CanMaximize = false` |
| centred | `WindowStartupLocation = CenterScreen` |
| close hides | `Closing` -> `e.Cancel = true; Hide()` |

`CanMaximize` is worth naming because it is not obvious it exists — Avalonia had only `CanResize` for
a long time, and "resizable but not maximizable" would otherwise have to be faked. It is present on
`Avalonia.Controls.Window` in 12.1 beside `CanMinimize` and `AllowedWindowActions`; read from
`ref/net10.0/Avalonia.Controls.xml` in the pinned package rather than assumed.

**Hide-on-close is conditional, and the condition matters.** `WindowClosingEventArgs` carries
`CloseReason`; only `WindowCloseReason.WindowClosing` — the user clicking the close button — is
cancelled and turned into `Hide()`. `ApplicationShutdown` and `OSShutdown` are let through, or Quit
could not close a window that is open and the process would hang on a window refusing to go. The
reference has no equivalent of this, because Tauri's close event carries no such distinction: it
hides unconditionally and relies on `app.exit()` bypassing the window layer.

**The window is created lazily, on first open, and kept afterwards.** The proposal said "created
hidden", which is `"visible": false` in `tauri.conf.json` — but that is a Tauri constraint mistaken
for a requirement: a Tauri window is a webview that must exist to receive IPC. Avalonia has no such
need, and this is a tray utility whose ordinary session never opens settings at all. Constructing six
views and their view models at startup would put XAML loading on the path between launch and the tray
icon appearing, for nothing. The instance is kept after the first open so that the selected tab and a
partly typed entry survive a hide.

**Light-only is enforced, not assumed.** The proposal makes dark theme a non-goal, but `FluentTheme`
follows the OS variant by default, so on a dark-mode machine "we didn't do dark theme" would render
as a dark window with light-theme colour choices in it. `RequestedThemeVariant = ThemeVariant.Light`
is set on the **window**: `TopLevel.RequestedThemeVariant` scopes it there, and the tray icon
deliberately has no theme handling at all (change 9), so there is nothing app-wide to state a variant
for. `Avalonia.Themes.Fluent` is already pinned in `Directory.Packages.props` and referenced by
nothing; this change is its first consumer, and `App.Styles` gains the `FluentTheme` that a window
needs and a tray icon never did.

`ShowInTaskbar` stays at its default of `true` while the window is open. The application is
tray-only, but a *visible window* that cannot be found in the taskbar or with Alt+Tab is worse than
the taskbar entry it would avoid.

### 5. Reaching the window from the tray

Two entry points, both landing in the same method:

- The existing `Settings` `NativeMenuItem`, whose handler currently logs "Settings chosen; there is
  no settings window yet" (`App.cs:107`).
- `TrayIcon.Clicked`, which change 9 explicitly deferred to this change. The event exists on
  `Avalonia.Controls.TrayIcon` in 12.1 — read from the reference assembly, not assumed.

Opening is `Show()` then `Activate()`, and `Activate()` is the part that matters: a hidden window
shown while another application has focus can come up behind it. Both handlers already run on the UI
thread, being Avalonia event handlers.

Whether `Clicked` fires on macOS when a `Menu` is attached is an open question, recorded below. On
Windows the left click and the menu are separate gestures; on macOS a status item with a menu
conventionally opens the menu on any click. If it does not fire, the menu item is the entry point on
that platform and nothing else changes.

### 6. Opening the log folder needs one seam, and no OS switch

`Core/Shell/ISystemShell.cs` declares `void OpenFolder(string path)`. `Platform/Shell/SystemShell.cs`
implements it as

```csharp
Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
```

and `AddNativeShell()` registers it, beside `AddNativeOutput()`. This is the first code in
`Pisum.Whisper.Platform` outside `Output/`.

**There is deliberately no `OperatingSystem.IsWindows()` switch in it.** .NET implements
`UseShellExecute = true` on macOS by handing the path to `/usr/bin/open`, which is precisely the
command the reference's `open_log_folder` runs, so one call covers both targets. That is unusual
enough for this codebase — every other Platform type is a Windows/macOS pair — that the macOS half is
verified by hand rather than trusted, in task 6.2.

The interface exists for one reason: `Process.Start` cannot be faked, and without the seam the
Logging tab's view model could not be unit tested at all. It is not there for a future second
implementation.

### 7. Providers and Presets

**Providers.** One `ProviderEntryViewModel` per `ProviderConfig`, plus Add and Remove on the list.

- New entries get `Id = Guid.NewGuid().ToString()`, matching the reference's `crypto.randomUUID()`.
- The API key box is a `TextBox` with `PasswordChar` set, cleared by the reveal toggle. The key is
  never logged, at any level — `CLAUDE.md`'s rule, and the reason `GeminiKeyProbe.Scrub` exists.
- The model dropdown is populated by `IGeminiKeyProbe.ListModelsAsync`, cached per API key for the
  lifetime of the window and cleared by Refresh. The reference caches per `providerType:apiKey`;
  there is one provider type here, so the key alone is the cache key.
- Test Connection calls `TestConnectionAsync` and renders `KeyProbeResult` inline. `KeyProbeResult`
  was designed for this: it "carries both the outcome and the text to show, so a failed test renders
  without wrapping a UI command in a `try`". No `try` is written around it.
- Every provider edit goes through `SettingsEditor.Edit`, so the debounce covers the key box.

**Presets.** This tab does **not** use `SettingsEditor`. `SettingsStore` already exposes exactly the
three operations the reference reaches through IPC — `SavePreset`, `DeletePreset`, `SetActivePreset`
— and they encode rules the view models must not duplicate: a built-in cannot be deleted
(`SettingsException`), a deleted active preset moves to its neighbour rather than back to the
default, and a built-in's edited name and prompt survive the next load's merge.

Those three methods mutate `SettingsStore.Current` in place and save it. That is the one place
Decision 2's clone can go wrong: a pending draft cloned *before* a preset was added would be saved
*after* it, and would silently delete it. So **every Presets command awaits `SettingsEditor.FlushAsync`
before calling the store**. One line per command, and a unit test per command asserting it.

Activating a preset is also what finally proves the `tray-icon` spec's *The active preset changes*
scenario, which change 9 shipped and could not exercise because nothing called `Save`.

### 8. The hotkey recorder

`IGlobalHotkeyService.CaptureAsync` does the capturing: it suspends normal matching, waits for one
complete combination, and returns a `HotkeyCapture` already in the shape `HotkeyBinding` accepts.
Three things it deliberately does *not* do, which the view model must:

**Require a modifier.** `GlobalHotkeyService.TryCapturePress` captures any non-modifier key with
whatever mask happens to be held, including none. A bare `K` is a legal capture and a terrible
hotkey. The view model rejects a capture whose modifier list is empty and stays in recording mode.

**Cancel on Escape.** Escape is in `KeyCodeMap`'s vocabulary, so `CaptureAsync` returns it as a
binding rather than treating it as a cancel. Rather than racing the hook against Avalonia's own key
handling — both see the keystroke, in no guaranteed order — the view model treats *a captured binding
of bare Escape with no modifiers* as the cancel. It is deterministic, needs no second input path, and
falls out of the modifier rule above: Ctrl+Escape stays bindable.

**Say why a key was refused.** `HotkeyCaptureOutcome.KeyNotSupported` is a third outcome, meaning
SharpHook reported a key this vocabulary cannot name and therefore cannot persist. It is rendered as
a message and recording continues.

**The recorder must not be startable when the hook is not running.** `CaptureAsync` completes only
from a hook callback. With `HotkeyAvailability.Failed` — the macOS missing-Accessibility case, which
`GlobalHotkeyService.StartAsync` handles by coming up without a hotkey — the returned task never
completes, and the UI would sit on "Press a key combination..." for ever. The Change button is
disabled and a banner names the state whenever `Availability != Available`. This is a *smaller* thing
than the "Rendering `HotkeyAvailability.Failed`" item change 9 deferred to change 11: it is a disabled
button rather than a notification, and it exists to prevent a hang rather than to inform.

The capture's `CancellationTokenSource` is cancelled when the user clicks Cancel and when the window
hides, so no capture outlives the UI that started it.

Conflicts: `ConflictDetector.ConflictsWithSystemHotkey` is consulted after a successful capture and
its result shown as a warning banner. It warns and never blocks — the table is a heuristic mixing
Windows and macOS shortcuts, and `ConflictDetector`'s own documentation says nothing in the runtime
path may consult it.

### 9. Audio, Logging, General

**Audio.** Two mutually exclusive buttons over `AudioFormat`. Nothing else.

**Logging.** Level (the five names `LogLevelNames` parses), max file size 1-100 MB, retention 1-365
days, the path from `LogDirectory.Path`, and Open Log Folder via Decision 6. The level applies
immediately; size and retention are read when the logger is *built*, in `AddFileLogging` before the
container exists, so they take effect at the next launch — and the UI says so, as the reference's
does. Saying it is not decoration: a settings window whose other fields all apply instantly teaches
the user that they all do.

**General.** Recording mode, max duration 10-3600 s, and the two flags nothing consumes yet.

Shipping `StartWithSystem` and `ShowTrayNotifications` as persist-only rows is a deliberate call.
Neither has a consumer until change 11, so toggling them today changes nothing observable. The
alternative — leaving the rows out until change 11 — was rejected because `AppSettings.StartWithSystem`
already **defaults to `true`** in a file no autostart code reads, so the untruth exists whether or not
the toggle does; the toggle at least lets the user record what they want before the code that honours
it lands, and change 11 is the next change. Recorded here so that a reviewer finding an inert toggle
finds the reason with it.

**Clamping.** Every numeric field clamps to its bounds and falls back to the minimum on an
unparseable value, matching the reference's `parseInt` guards. The clamp is in the view model, so it
is unit tested without a UI.

### 10. `GeminiProvider.DefaultModel` needs a public home

The model dropdown's empty option reads "Default (gemini-2.5-flash-lite)", matching the reference.
That string is `GeminiProvider.DefaultModel`, an `internal const` on an `internal` class — the App
project cannot see it, by the same design that hides the whole provider behind `GeminiProviderPool`.

Rather than duplicating the literal in XAML, add `public static class GeminiDefaults` in
`Core/Transcription/` holding `public const string Model = "gemini-2.5-flash-lite"`, and point
`GeminiProvider.DefaultModel` at it. This is change 8's task 1.1 pattern exactly — the sample rate
had the same problem, and was solved by giving the constant one public home rather than by widening
the type that held it.

### 11. Testing

**Most of this is testable without Avalonia.** The view models hold observable properties and call
into `SettingsStore`, `SettingsEditor`, `IGeminiKeyProbe`, `IGlobalHotkeyService` and `ISystemShell`
— all of which are already interfaces or already have explicit-dependency constructors. Those tests
are ordinary `[Fact]`s over FakeItEasy fakes: clamping, the modifier rule, the Escape rule, the
disabled recorder, the flush-before-preset-command rule, the debounce coalescing, and the cache and
Refresh behaviour of the model list.

**`[AvaloniaFact]` is for what needs a visual tree**: that the window has six tabs, that closing it
hides rather than closes it, that `CloseReason.ApplicationShutdown` is *not* cancelled, and that each
tab's XAML loads and binds — which is the failure a view-model test cannot reach, because a misspelled
binding path is a runtime warning, not a compile error.

Two properties of the headless runner shape this, both read from the package rather than assumed:

- `HeadlessUnitTestSession` runs every `[AvaloniaFact]` through **one dispatcher loop**, so they
  serialize with each other regardless of xUnit's parallel-by-class. `CLAUDE.md`'s parallelism note
  still holds for the other two test projects; this one is simply slower per test, and there are few
  of them.
- The default isolation level is `AvaloniaTestIsolationLevel.PerTest` — a fresh `Application` and
  `Dispatcher` per test method. It is left at the default. `PerAssembly` is faster and explicitly
  warns that "tests must not rely on any global or persistent state", which a settings window backed
  by a file-writing singleton is exactly the wrong candidate for.

The assembly declares `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`
(`Avalonia.Headless.AvaloniaTestApplicationAttribute`, taking a type that exposes
`static AppBuilder BuildAvaloniaApp()`). It cannot point at `Pisum.Whisper.App.App`, whose constructor
takes an `IServiceProvider` and whose `OnFrameworkInitializationCompleted` resolves a
`DictationOrchestrator` and creates a tray icon. `TestAppBuilder` builds a bare `Application` carrying
only `FluentTheme`, which is all a window needs.

**Category traits.** The new project gets its own `Traits.cs`, as both existing ones have.
`CLAUDE.md`'s rule is mechanical and unchanged: a test whose constructor reaches a temp path or builds
a container is `Integration`, everything else is `Unit`. View-model tests over fakes are `Unit`;
anything constructing a real `SettingsStore` over a temp file is `Integration`.

### Rejected alternatives

- **Rebuild the provider pool on save.** The reference's shape. Rejected in `CLAUDE.md` before this
  change opened, and it would reintroduce the mid-transcription race the pool's per-call snapshot
  prevents. (Decision 1.)
- **Bind the views to `SettingsStore.Current` directly.** Simplest to write, and it makes a half-typed
  API key visible to a dictation in progress. (Decision 2.)
- **Save on every keystroke, as the reference does.** 78 file operations and 39 `Changed` events for
  one pasted key. (Decision 2.)
- **Put the view models in `Core`.** `Core` is declared free of UI dependencies, and change 9 set the
  precedent one change earlier by keeping the tray mapping entirely in `App.cs`. (Decision 3.)
- **Create the window hidden at startup.** A Tauri constraint mistaken for a requirement; it puts XAML
  loading in front of the tray icon for a window most sessions never open. (Decision 4.)
- **Let `FluentTheme` follow the OS.** Silently turns "no dark theme" into "an untested dark theme".
  (Decision 4.)
- **A second, window-local keyboard hook for the recorder.** libuiohook keeps one static callback per
  process and a second concurrent hook corrupts its state — which is why `CaptureAsync` exists at all.
  (Decision 8.)
- **Handling Escape from Avalonia's key events instead of from the capture.** Both the hook and the
  focused window see the keystroke, in no guaranteed order, so it would be a race. (Decision 8.)
- **A Windows/macOS pair for `ISystemShell`.** `UseShellExecute = true` covers both. (Decision 6.)
- **Widening `GeminiProvider` to public for one string.** The provider is internal on purpose; a
  public constant is the smaller change. (Decision 10.)
- **`AvaloniaTestIsolationLevel.PerAssembly`.** Faster, and it documents itself as unsafe for tests
  touching global state. (Decision 11.)

## Risks / Trade-offs

- **The macOS window is unverified.** `MacOSPlatformOptions.ShowInDock = false` makes this an
  accessory-policy application, and whether a window it shows takes keyboard focus without a Dock icon
  is not established. It joins the existing Apple Silicon debt: change 8's tasks 6.1 and 6.4 and
  change 9's tasks 4.1-4.3 are all waiting on the same sitting. Task 6.2 covers it.
- **An edit can be lost inside a 400 ms window** if the process dies there. Mitigated by flushing on
  hide and on exit; not eliminated. The reference cannot lose an edit this way, and this is the price
  of not writing a file per keystroke.
- **Headless tests do not render.** They catch a XAML file that fails to load and a binding that
  throws, not one that silently binds to nothing. Layout and legibility stay manual (tasks 6.1, 6.2).
- **`Avalonia.Headless.XUnit` pins the test framework.** It is compiled against `xunit.v3` 3.2.2,
  which is why `Directory.Packages.props` pins that version; the declared range is `[3.2.2, )`, so
  NuGet would happily resolve 4.0.0 against a major it was not built for. The pin is load-bearing and
  the comment beside it should say so.
- **This is the largest single change in the sequence** and the first with meaningful UI. Six tabs is
  six chances to apply the debounce, the clone and the flush rules inconsistently; the task list keeps
  each tab's code and its tests in one task for that reason.

## Open Questions

1. **Does `TrayIcon.Clicked` fire on macOS when a `Menu` is attached?** On Windows the click and the
   menu are separate gestures. On macOS a status item with a menu conventionally opens the menu on any
   click, and Avalonia's `AvnTrayIcon` may never raise `Clicked`. If it does not, the Settings menu
   item is the macOS entry point and no code changes. Settled by task 6.2.
2. **Does a window shown by an accessory-policy application take focus?** `Show()` plus `Activate()`
   is the standard answer; whether it is sufficient with `ShowInDock = false` is not known. Settled by
   task 6.2, and it is the one question that could force a design change — an application that must
   call `NSApp.activate(ignoringOtherApps:)` needs a Platform seam this design does not have.
3. **Is 400 ms right?** Chosen, not measured. Task 6.1 is the first time anyone types a real API key
   into it; if the commit feels laggy, or a fast tab-away loses an edit, the constant moves once and
   the reason is recorded here.
