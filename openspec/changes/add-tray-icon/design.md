## Context

The application already has a tray icon. Change 1 shipped one in `src/Pisum.Whisper.App/App.cs` —
a single idle image, the fixed tooltip `"Pisum Whisper"`, a one-item menu holding Quit, and a
`Dispose` on exit — because a tray-only process with no icon cannot be quit. So this change edits a
live file rather than adding a component, and roughly half of it is deletion and replacement.

That existing icon is **unspecified**. `openspec/specs/application-host/spec.md` mentions the tray
twice and owns it neither time: once in a negative scenario (a container failure "does not reach the
point of showing a tray icon"), and once under *Verified platform stack*, whose subject is whether
Avalonia's `TrayIcon` works at all, not what this application draws in it. `tray-icon` therefore
claims the behaviour outright and `application-host` is left alone — its two mentions stay true.

The reference is `W:\github-pisum-transcript\src-tauri\src\tray.rs`, 260 lines of which this change
re-expresses three functions: `setup_tray`, `set_recording_state` and `set_tray_tooltip`.
`send_notification` and its `showTrayNotifications` gate belong to change 11 and are not touched
here.

**The proposal predates change 8 and the macOS spike run, and has drifted in three places.** Each is
corrected in the decisions below rather than silently ignored:

| The proposal says | What is true now |
|---|---|
| "two icon states, idle and recording" | change 8 publishes three (`DictationState`), and its own documentation argues at length against collapsing them — see *Three icons* below |
| select a light or dark icon variant through `PlatformSettings.GetColorValues()` and `ColorValuesChanged` | there is no theme probe at all; macOS delegates to AppKit and Windows carries its contrast in the art |
| "prefer a template image and let AppKit invert it **if** Avalonia exposes that (spike S3)" | Avalonia 12.1.1 does expose it. It is undocumented, which is why the question was left open — see *Template images* below |
| macOS `NSStatusItem` may not present tooltips, in which case the preset name moves into the menu | S3 on an Apple M4 recorded **PASS**, "tooltip visible on hover", by direct observation. The tooltip stays a tooltip |

Facts below marked *from the binaries* were read out of `W:\_nuget\packages` during design, from
Avalonia 12.1.1 as pinned in `Directory.Packages.props`.

## Goals / Non-Goals

**Goals:**
- The user can tell, at a glance and without hovering, whether the application is idle, recording, or
  transcribing.
- The active preset is discoverable without opening anything.
- Settings and Quit are reachable from the icon.
- One drawing per state serves both platforms, and neither platform probes a theme.
- Nothing in the presentation path can block the hotkey, the dispatch loop, or shutdown.

**Non-Goals:**
- The settings window (change 10). The Settings item is a stub that logs and does nothing.
- Notifications, the `showTrayNotifications` gate, and first-run discoverability messaging (change 11).
- A recording HUD, overlay, waveform, audible cue, or transcript history in the menu.
- Animation of any kind. A menu bar extra that animates is discouraged on macOS and would need a
  `DispatcherTimer` swapping frames for no information gain; `Transcribing` is a distinct static
  glyph instead.
- Any change to the settings schema, and any change to `Core/Dictation/`. This change consumes
  `DictationOrchestrator.StateChanged` and `SettingsStore.Changed` as they already stand.

## Decisions

**Three icons, not two.**
`DictationState` has three values and the tray renders all three. Mapping three onto two forces a
choice between two lies: `Transcribing → recording icon` reproduces exactly the defect
`DictationState`'s own remarks single out in the reference — `tray::set_recording_state(false)` is
called after the paste (`hotkey/manager.rs:329`), so its icon claims to be recording throughout the
upload — while `Transcribing → idle icon` inverts the same confusion, telling a user in toggle mode
that nothing is happening right before the hotkey answers "Transcription In Progress". Change 8 built
the third value deliberately and paid nothing for it, because the orchestrator must already tell
recording from transcribing to interpret a press. Collapsing it here would spend effort to discard
what is known.
*Alternative rejected:* two icons plus a state-bearing tooltip. It hides the distinction behind a
hover, on the one platform where a tray icon is most often glanced at and least often hovered.

**No theme handling, on either platform.**
There is no theme probe, no `ColorValuesChanged` subscription, no `ActualThemeVariantChanged`
subscription, and no light/dark asset variants. The two platforms reach that outcome differently and
the difference is the whole macOS story below.

*From the binaries:* Avalonia's Windows backend reads the theme through WinRT
`Windows.UI.ViewManagement.UISettings` and `UIColorType` — `Avalonia.Win32.dll` contains no
`AppsUseLightTheme`, `SystemUsesLightTheme` or `Personalize` string at all. That reports the **apps**
theme, the same value the reference reads from the registry. Windows 11's taskbar follows
`SystemUsesLightTheme`, a different key, which under "Custom" mode can be Dark while apps are Light.
So both the reference's mechanism and the one the proposal specified pick the icon for the wrong
background in that configuration, and neither route available in Avalonia can reach the right one.
Rather than add a native probe in `Pisum.Whisper.Platform` for it — which would have no macOS
counterpart and would need to justify itself against the clipboard precedent — the contrast moves
into the art. See *The glyphs*.
*Alternative rejected:* a `SystemUsesLightTheme` probe in `Pisum.Whisper.Platform`. Correct on
Windows, unavailable on macOS, and it buys a mechanism where a drawing suffices.
*Alternative rejected:* matching the reference exactly and documenting the Custom-mode gap. Cheaper
to write and strictly worse to look at.

**macOS uses template images, which is where its theme handling goes.**
*From the binaries:* Avalonia 12.1.1 exposes tray template images, undocumented — and **not on
`TrayIcon`**. It is an attached property owned by `Avalonia.Controls.MacOSProperties`, which carries
`AttachedProperty<bool> IsTemplateIconProperty` with `void SetIsTemplateIcon(TrayIcon, bool)` and
`bool GetIsTemplateIcon(TrayIcon)`, so the call site is
`MacOSProperties.SetIsTemplateIcon(trayIcon, true)`. Dispatch reaches the backend through the
`ITrayIconWithIsTemplateImpl` platform interface; `Avalonia.Native.dll` carries the interop;
`libAvaloniaNative.dylib` carries `AvnTrayIcon::SetIsTemplateIcon(bool)` and calls the `setTemplate:`
selector alongside `NSStatusBar` and `statusItem`. It is absent from the package's XML documentation
because it has no `///` comment, which is why S3 recorded the question as open.

An earlier draft of this design placed those accessors on `TrayIcon` itself. That is recorded rather
than quietly corrected, because the mistake is self-concealing: `TrayIcon` carries no member matching
`Template` in either `lib/net8.0` or `ref/net8.0`, so anyone who goes looking there finds nothing and
reads it as "Avalonia does not expose this", which is the very conclusion S3 left open.

So on macOS the icon is flagged as a template and **AppKit** does the adaptation: it tints the glyph
for a light or dark menu bar, respects vibrancy and Reduce Transparency, and inverts it correctly
under the click highlight — which a coloured non-template image, drawn as-is over the accent-tinted
highlight, does not. This is both the Apple-recommended treatment for a menu bar extra and less code
than probing a theme, so the two goals do not trade off.

The flag is set only under `OperatingSystem.IsMacOS()`, and that guard is now documentation rather
than a hedge. *From the binaries:* `Avalonia.Native.TrayIconImpl` implements
`ITrayIconWithIsTemplateImpl, ITrayIconImpl, IDisposable`, while `Avalonia.Win32.TrayIconImpl`
implements `ITrayIconImpl, IDisposable` and nothing else. The property's change handler reaches a
backend only through that interface, so on Windows the call sets a value no implementation reads.
The guard stays because it says at the call site *why* the line exists, not because the outcome is
in doubt. This closes what was Open Question 2. One inch is left unwalked and is worth naming:
`MacOSProperties.TrayIconIsTemplateIconChanged` was not disassembled, so that the handler is a plain
interface test is inference — from an interface that exists for no other purpose. Task 4.3 confirms
it by running.

**One drawing per state, exported twice; every interior mark is a hole.**
A template image is rendered from its **alpha channel alone**; colour is discarded. Interior detail
must therefore be genuine transparency, not a light-coloured fill. The reference gets this wrong:
`icons/tray-iconTemplate.svg` fills its four text lines `#ffffff` under a comment reading "pure black
on transparent", so its macOS icon almost certainly renders as a featureless filled bubble. This
change does not re-express that.

Drawing the marks as holes is what lets one geometry serve both platforms, and it is the same
property that removes the Windows theme probe: a hole reads against the bubble whatever colour is
behind it. The two exports differ only in the fill of the outer path.

**The glyphs.**
The reference's speech bubble is kept — same rounded body, same bottom-left tail — redrawn on a
16-unit grid so coordinates land on whole or half pixels at the size actually rendered. Interiors
carry the state, redundantly in silhouette *and* hue, because on macOS the template path leaves only
silhouette:

| State | Interior | Windows fill |
|---|---|---|
| `Idle` | three transcript lines, 1.3 units tall | `#6B7A8F` slate |
| `Recording` | one record dot, r 2.4 | `#FF3B30` — Apple systemRed, as the reference uses |
| `Transcribing` | an ellipsis, three dots r 1.1 | `#C77F0A` amber |

Two values were settled by rendering rather than by arithmetic. Interior lines are **1.3** units,
not 1.1: at 1.1 the three stripes and their gaps both fall near one physical pixel at 16 px and grey
into a smudge. Idle is `#6B7A8F` after testing three slates — a deeper one dies on a dark taskbar, a
lighter one washes out on a light one, and this sits near the symmetric-contrast optimum at 4.4:1 on
white and 3.7:1 on `#202020`, both clear of the 3:1 non-text floor. Recording and transcribing clear
it on both backgrounds as drawn.

Twelve files land in `src/Pisum.Whisper.App/Assets/`: `tray-{idle,recording,transcribing}.png` at
32x32 for Windows, `tray-{idle,recording,transcribing}Template.png` at 36x36 for macOS, and the six
`.{win,mac}.svg` sources they are exported from. The `Template` suffix follows the AppKit convention
and is **documentation only** — that convention fires for images loaded by bundle resource name, and
Avalonia builds its `WindowIcon` from a stream, so `IsTemplateIcon` must still be set explicitly.

Note that `Pisum.Whisper.App.csproj` globs `<AvaloniaResource Include="Assets\**" />`, so the six
SVG sources are embedded in the assembly along with the PNGs — about 7 KB. Excluding them is a
one-line glob change if that is judged worth making; it is recorded here so it is a choice rather
than an oversight.

**Updates are marshalled, and the tray unsubscribes before it disposes.**
`DictationOrchestrator.StateChanged` is raised on a pooled thread and `SettingsStore.Changed` on
whichever thread called `Save` — change 10's settings window will call it from the UI thread. Both
are marshalled with `Dispatcher.UIThread.Post`, which preserves order at equal priority, so a fast
dictation's `Idle` cannot overtake its own `Transcribing`.

`OnExit` unsubscribes from both events before it disposes the `TrayIcon`, but **not** for the reason
that ordering suggests, and the real one is worth writing down because the plausible one is wrong.
`Program.cs` runs `host.StopAsync` inside a `finally` that executes **after**
`StartWithClassicDesktopLifetime` returns, so by the time `DictationOrchestrator.StopAsync` announces
`Idle` the dispatcher loop is already gone:

```
Quit -> desktop.Shutdown() -> Exit -> OnExit: unsubscribe, Dispose(), _trayIcon = null
     -> StartWithClassicDesktopLifetime returns        <-- dispatcher loop is dead here
     -> finally { host.StopAsync(5s) }
     -> DictationOrchestrator.StopAsync -> Announce(Idle) -> Dispatcher.UIThread.Post(...)
                                                                 -> queued into a loop
                                                                    nobody pumps; never runs
```

That announcement cannot arrive at a disposed icon, because it cannot arrive at all. What remains is
narrower: an announcement posted by a *pipeline task* on a pooled thread in the window between the
Quit click and the loop stopping. What covers that one is `_trayIcon = null` and the null-conditional
in the handler, which is why both stay. Unsubscribing first is kept because it costs a line and stops
pointless posts — tidiness that happens to be free, not the invariant it looks like.

The consequence for verification is the part that bites: `StopAsync` runs after `OnExit`, so log
lines — the clipboard restore among them — **do** appear after the tray's release line, and that is
change 8's `StopAsync`-awaits requirement working, not a leak. Task 3.3 asserts their presence.

**`App` becomes the orchestrator's first consumer.**
It resolves `DictationOrchestrator` from `IServiceProvider` in `OnFrameworkInitializationCompleted`,
which runs after `host.Start()`, so the singleton exists. `Program.cs` currently comments that the
orchestrator is registered as a hosted service so the host constructs it, "nothing else resolves it
yet" — that comment stops being true here and is updated with it.

**The tooltip is `"Pisum Whisper - {active preset name}"`,** refreshed on `SettingsStore.Changed`,
matching the reference's `set_tray_tooltip`. It does not carry the state: the icon does that, and a
tooltip that changes under the pointer is worse than one that does not. The refresh is wired here and
cannot be exercised here — see *The tooltip's refresh path ships unexercised* below.

**The menu is Settings, separator, Quit,** as the reference builds it. Settings logs and returns
until change 10; Quit calls `desktop.Shutdown()` as it does today.

## Risks / Trade-offs

**The three PNG sizes are provisional on macOS.** 32x32 for Windows is the 16 px slot at 200% DPI and
matches the asset change 1 shipped. 36x36 for macOS is 18 pt at @2x, but how Avalonia sizes an
`NSImage` built from a `WindowIcon` stream is unverified: if it takes pixel dimensions as points, 36
asks for a 36 pt glyph in a 24 pt menu bar. `WindowIcon` also takes a single bitmap, so a proper
multi-representation `NSImage` may not be expressible at all. Both are Open Questions; neither blocks
the Windows half.

**This change has no automated tests, and that is a real gap.** There is no
`tests/Pisum.Whisper.App.Tests`, and `Avalonia.Headless.XUnit` is deliberately not referenced yet —
change 10 adds it, and the `migrate-tests-to-xunit-v3` migration that unblocks it says so explicitly.
Standing it up here would pull change 10's dependency forward; a pure decision function in `Core`
whose only consumer is one file in `App` would invent an abstraction for a one-line map. Both were
weighed and neither is taken: verification is manual, per the task list, and change 10 inherits glue
that headless tests can then cover alongside the window. This is the weakest part of the change and
should be read as a deferral, not a judgement that it does not matter.

**The tooltip's refresh path ships unexercised.** `SettingsStore.Changed` is raised from `Save`
alone; `Load` writes through `Write` and never raises it, including the first-launch write and the
dangling-`activePresetId` repair; and there is no `FileSystemWatcher` anywhere in `src/`. Nothing in
the application calls `Save` at runtime until change 10's settings window, so in this change the
subscription is wired, marshalled and unsubscribed correctly and **cannot be made to fire**. Editing
`~/.pisum-whisper.json` by hand and relaunching proves the interpolated *string*, not the
subscription that would refresh it.

The subscription is written here anyway rather than deferred with its verification, on two grounds:
`OnExit`'s ordering has to account for it either way, and change 10 needs it on the day it opens the
window this menu points at. What defers is only the check — task 4.1 now verifies the initial tooltip
and says so. The spec's *The active preset changes* scenario is therefore satisfied by construction
and unproven until change 10 supplies something that calls `Save`; it is the same shape of gap as the
missing automated tests above, and it is deliberate for the same reason.

**Windows 11 files a newly registered tray icon into the hidden overflow flyout**, so the first-run
experience of the change whose purpose is "the user can see it is running" is that they cannot. There
is deliberately no API to promote an icon out of the overflow. macOS has the same failure from a
different cause: on notched MacBooks menu bar extras are pushed toward the notch and can become
unreachable. Both belong to change 11's notification transport, gated on the `SettingsStore`
`IsFirstLaunch` flag that already exists; neither is fixable here.

**A failed hotkey is still invisible.** `HotkeyAvailability.Failed` — the macOS case where an absent
Accessibility grant lets the process start with no hotkey and, by design, no retry — is not rendered.
The tray is the only surface this application has, so it is the natural home for saying so, but the
state is not a `DictationState` and adding a fourth icon for it would widen this change past its
proposal. Recorded here so change 11 inherits it as a known gap rather than discovering it.

## Open Questions

- **How does Avalonia size a macOS tray image built from a `WindowIcon` stream?** Decides whether
  36x36 is right, and whether @2x is expressible at all. Falsified by a glyph that renders oversized,
  clipped, or blurry in the menu bar. Needs Apple Silicon hardware; extend the existing `tray` spike.
- ~~**Is `IsTemplateIcon = true` a harmless no-op on Windows?**~~ **Answered from the binaries.**
  `Avalonia.Win32.TrayIconImpl` does not implement `ITrayIconWithIsTemplateImpl` and
  `Avalonia.Native.TrayIconImpl` does; see *macOS uses template images* above for what that leaves
  open, which is the change handler's body and nothing else.
- **Does the reference's macOS template icon in fact render as a featureless blob?** The white-fill
  reading of `tray-iconTemplate.svg` is certain; that it produces a solid bubble follows from how
  AppKit renders template images and has not been observed. It changes nothing here — the rule this
  change follows is right either way — but it is the kind of claim worth confirming before it is
  repeated.
- **Does `spikes -- tray` still reproduce under both appearance modes with the template flag set?**
  S3 left template support "unconfirmed — only tested under Light". The API half is now answered from
  the binaries — and answered twice, the second time correcting the first about which type owns the
  accessors — but the visual result is not.
