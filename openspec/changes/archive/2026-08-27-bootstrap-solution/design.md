## Context

The repository holds `Pisum.Whisper.slnx` with no projects, a `global.json` pinning SDK 10.0.400, and
OpenSpec tooling. There is nothing to build.

The product is a re-creation of `W:\github-pisum-transcript`, a Tauri 2 (Rust) + Svelte 5 application.
That reference is the behavioural specification, but none of its code transfers: the port replaces
Rust crates with .NET libraries across the board. Three of those replacements carry behaviour that
cannot be assumed, and the whole architecture rests on them:

- a global keyboard hook that reports key **release**, not just press;
- microphone capture that works identically on WASAPI and CoreAudio;
- a menu-bar icon on macOS driven from a cross-platform UI toolkit.

Building the solution skeleton is cheap. Discovering in change 8 that one of these does not work is
not. This change therefore does both: it lays down the skeleton and proves the stack.

## Goals / Non-Goals

**Goals:**
- A solution that builds cleanly on both win-x64 and osx-arm64 from one checkout.
- A composition root that fails fast on a bad registration.
- A process that is menu-bar-only from the very first run, so the presentation model is never retrofitted.
- Binding evidence for or against SharpHook, SoundFlow, Avalonia's tray, and Concentus.

**Non-Goals:**
- Any dictation behaviour: no hotkey handling, audio, Gemini calls or output.
- A settings window; the tray menu may be a stub.
- Packaging, signing or installers.

## Decisions

**Single `net10.0` target framework for every project, including the platform-specific one.**
Everything OS-specific in this app is P/Invoke or `Process.Start`; nothing needs WinForms, WPF or
AppKit bindings, because Avalonia supplies the tray icon. Runtime `OperatingSystem.IsWindows()`
checks plus `[SupportedOSPlatform]` attributes are sufficient.
*Alternative rejected:* `net10.0-windows` and `net10.0-macos` targets with RID-conditional project
references. That splits the build graph, makes the solution fail to load on the other platform, and
buys nothing here.

**One `Pisum.Whisper.Platform` project rather than one per OS.** The genuinely platform-specific
surface is small — autostart, notifications, opening a folder, a macOS permission check. Two projects
for that is more ceremony than code.
*Alternative rejected:* `Platform.Windows` + `Platform.MacOS`, which would force conditional
references in the app project.

**Central package management** via `Directory.Packages.props`. Four projects sharing Avalonia,
Serilog and the test stack will drift otherwise. The spiked stack — SharpHook 8.0.0, SoundFlow 1.4.1,
Concentus 2.2.2, Concentus.Oggfile 1.0.7 — is pinned here even though nothing references it until
changes 4, 6 and 7, so the spikes and the production code that follows them cannot drift apart.

**A repository-local `NuGet.config` clears inherited package sources.** The developer machine's
user-level configuration has `nuget.org` commented out and restores from private Cortado feeds that
GitHub Actions cannot reach, so without this the repository builds locally and fails in the CI added
by `add-packaging-ci`. Two active sources also trip NU1507 under central package management, which
warnings-as-errors turns into a build failure.
*Alternative rejected:* suppressing NU1507. The warning exists to prevent a package id resolving from
either of two feeds — the dependency-confusion case — so silencing it trades a build error for a
supply-chain risk. Package source mapping was also rejected: it satisfies the analyser but still
leaves CI pointed at feeds it cannot reach.

**Avalonia configured menu-bar-only from the start**, through
`MacOSPlatformOptions { ShowInDock = false }`. The reference achieves this with an objc2 call to
`NSApplication.setActivationPolicy(Accessory)`; Avalonia exposes it directly, so no interop is needed.

**Warnings as errors, nullable enabled.** Cheap at four empty projects, expensive to introduce at
thirty populated ones.

**Spikes stay in `spikes/`, excluded from the solution, until this change archives.** Amended from
"throwaway and are not merged": Windows and macOS verification are now separated by an unknown
interval, so discarding the harness means rewriting four spikes to answer questions whose harness had
already been written. Excluding `spikes/` from `Pisum.Whisper.slnx` preserves the original intent —
no spike abstraction reaches `src/` — while leaving the macOS half re-runnable rather than
re-writable. The directory is deleted when this change archives.

**Spikes gate the change.** The skeleton may land first, but this change is not complete until all
four have a recorded outcome, because later proposals are written against the stack they prove.

## Risks / Trade-offs

- **SharpHook does not deliver reliable key-release, or its macOS run loop conflicts with Avalonia's.**
  → This is the highest-severity risk in the project: hold-to-record cannot be built on
  `RegisterHotKey` or `RegisterEventHotKey`, which are press-only. Mitigation: run S1 first, before
  any other work. Fallback is hand-written `WH_KEYBOARD_LL` and `CGEventTap` interop, which is
  significant effort and must be discovered now rather than in change 8.

- **SoundFlow cannot open a capture device at 48 kHz mono, or does not resample from the device rate.**
  → The design deletes the reference's entire sinc-resampling stage on the assumption that miniaudio
  converts. If it does not, `NAudio.Core`'s managed `WdlResampler` is added behind the same
  `IAudioCapture` interface. Contained, but must be known before `add-audio-pipeline` is designed.

- **SoundFlow is a large dependency for one job** — it also carries MIDI, synthesis, editing and
  steganography. → Accepted for now; the alternative is PortAudioSharp2 with native assets to ship
  per RID, or hand-written CoreAudio interop. Revisit only if it causes packaging problems.

- **Avalonia 12.1 is a recent major line.** → It is a stable release with a real `net10.0` target.
  One known wrinkle: DevTools moved out of `Avalonia.Diagnostics`, which stops at 11.3.20. Use
  `ProDiagnostics` 12.1.x or go without. Avalonia 11.3 remains a low-friction fallback if 12.1
  causes trouble, since the API surface used here is small.

- **macOS Accessibility grants are bound to the code signature**, so an unsigned binary re-prompts on
  every rebuild and makes S1 painful to iterate on. → Establish a stable development signing identity
  during this change rather than deferring it to packaging.

## Platform verification

This change landed Windows-verified with every `osx-arm64` cell deferred for lack of Apple Silicon
hardware. Hardware became available (issue #15) and the spikes were re-run on an Apple M4 running
macOS 26.6.2; results below replace the deferred cells. Two rows now carry a genuine **FAIL** — the
distinction from *deferred* matters here because both trigger their documented fallback.

| Spike | What must be demonstrated | win-x64 | osx-arm64 | Fallback if it fails |
|---|---|---|---|---|
| S1 | Global hook reports key press **and** release | **PASS** | **PASS** | Hand-written `WH_KEYBOARD_LL` / `CGEventTap` interop |
| S1 | Hook co-exists with Avalonia's run loop | **PASS** | **PASS** | Threading redesign; highest severity in the project |
| S1 | `EventSimulator` paste accepted by a foreign app | **PASS** | **FAIL** — see macOS spike results | Clipboard-only output with a manual-paste message |
| S2 | Capture opens at 48 kHz mono f32, resampling from the device's native rate | **PARTIAL** | **PASS** | `NAudio.Core`'s managed `WdlResampler` behind `IAudioCapture` |
| S3 | Tray icon visible; image swappable at runtime | **PASS** | **PASS** | Platform-specific tray implementation in `Pisum.Whisper.Platform` |
| S3 | Tooltip on `NSStatusItem` | n/a | **PASS** | Active preset name moves into the menu itself |
| S3 | Template image support (auto light/dark tinting) | n/a | unconfirmed — only tested under Light | Active preset name moves into the menu itself |
| S4 | Encoded `.opus` decodes back to the same duration | **PASS** | covered by win-x64 — pure computation, no platform surface | Hand-rolled Ogg muxer, as originally planned |
| 3.3 | `MacOSPlatformOptions { ShowInDock = false }` suppresses the Dock icon | n/a | **PASS** | None needed |
| 1.4 | Accessibility/Input Monitoring grant survives a rebuild | n/a | **PASS when launched through Rider** — see 2026-09-02 re-test | Stable signing identity, deferred to change 12 packaging (only matters outside Rider's ancestry) |

### Windows spike results

Run from `spikes/Pisum.Whisper.Spikes` — `hook`, `paste`, `audio`, `opus`, `tray`, `combined`.

**S1 — both key edges, PASS.** Press and release are both reported while a foreign application has
focus, and the modifier mask is correct on every edge: it carries `LeftCtrl, LeftShift` on the Space
press and sheds each modifier as it is released, which is exactly the state the hotkey matcher needs.
Caveat: the events were injected with `EventSimulator`. A low-level hook observes injected and
physical events on the same path, so this exercises the real code, but the hardware scan-code route
is unproven and is worth ten seconds of a human pressing the key.

**Finding that changes `add-text-output`.** SharpHook flags injected events — `EventMask.SimulatedEvent`
and `KeyboardHookEventArgs.IsEventSimulated`. Change 7 sends a synthetic Ctrl+V while change 6's hook
is live, so without that flag the app would observe its own paste keystroke. The flag makes ignoring
it a one-line check rather than a suppression scheme.

**S1 — co-existence with Avalonia, PASS on Windows.** The `combined` spike runs the hook on its own
thread while Avalonia owns the main thread, and marshals hook events through `Dispatcher.UIThread.Post`
to swap the tray icon: 9 presses, 9 releases, 6 icon updates, no contention. **This is the spike to run
first on a Mac** — it is the shape changes 6, 8 and 9 all take.

**S1b — paste into a foreign application, PASS.** A token was placed on the clipboard, pasted into
Notepad, the clipboard then overwritten with a sentinel, and select-all/copy returned the token. The
sentinel step is what makes this airtight: an empty Notepad cannot produce a false pass.

**S2 — capture, PARTIAL.** The format the product actually asks for is delivered exactly: 48 kHz mono
f32 from a device whose native format is stereo 48 kHz, 100.3% of the expected sample count, uniform
480-sample buffers, so channel downmixing works and costs nothing. Rate conversion is *performed* —
a 16 kHz request was honoured — but under-delivers: 93.9% of expected, from 94 callbacks per second
instead of 100, with buffers still uniformly 160 samples. Nothing is partially filled; the stream
simply runs slow.

This machine has **no non-48 kHz audio endpoint at all** — one capture device and three playback
devices, every one of them natively 48 kHz — so **the production direction (44.1 kHz device → 48 kHz
request) could not be tested.** WASAPI loopback capture was considered as a way to obtain a
differently-clocked source and does not help for the same reason. Closing this needs one of: a
44.1 kHz USB microphone, temporarily setting a playback endpoint to 44.1 kHz in Windows sound
settings and capturing it via loopback, or a virtual audio device. The first two are cheap; all
three change the machine, so none was done unasked. Consequence for `add-audio-pipeline`:
deleting the reference's resampling stage is safe on a 48 kHz-native device, which is the common case,
but is *not yet justified* in general. Re-measure on a 44.1 kHz input before relying on it; a 6%
shortfall would drop roughly 36 seconds from a ten-minute dictation.

**S3 — tray icon, PASS.** Icon shown, tooltip set, three-item native menu, eight runtime image swaps,
and a screenshot of the notification area caught the swapped (red) variant, so the replacement reaches
the shell rather than only the object. Two incidental findings: Windows 11 places a newly registered
tray icon in the **hidden overflow** by default, which is a first-run discoverability problem for
`add-tray-icon` and `add-system-integration`; and `Application.ActualThemeVariant` reports the theme
directly, which is simpler than the `TopLevel.PlatformSettings.GetColorValues()` route this design
assumed.

**S4 — Ogg/Opus, PASS.** `Concentus.Oggfile` 1.0.7 encodes against `Concentus` 2.2.2 and produces a
well-formed stream — `OggS`, `OpusHead` v1, `OpusTags` — which its own reader decodes back to 100.2%
of the source duration over 201 packets. No ffmpeg was needed. **But the OpusHead pre-skip is 0, not
the 312 that `add-audio-pipeline`'s proposal specifies.** Pre-skip declares the encoder's priming
delay; at 0 the decoder keeps roughly 6.5 ms of priming at the start. For dictation uploaded to
Gemini that is inaudible and irrelevant, so the recommendation is to drop the pre-skip requirement
rather than hand-roll a muxer to satisfy it — but it is a deliberate deviation, not an oversight.

**Task 1.9 — the macOS run-loop question, researched.** SharpHook's own documentation settles the
expectation. `UioHookResult.ErrorGetRunLoop` is documented as "`CFRunLoopGetCurrent` has failed
(macOS)", so libuiohook attaches its `CGEventTap` to the run loop of **whichever thread calls it**,
not specifically the main thread; and `RunAsync` is documented as running the blocking native API on
a separate thread. Expectation: the hook and Avalonia co-exist on macOS as they do on Windows, with
Avalonia owning the main thread's loop and SharpHook owning a background thread's. What would
falsify it: a `HookException` carrying `ErrorGetRunLoop` or `ErrorCreateRunLoopSource` from a
background thread, or a tap that starts successfully but never delivers callbacks. Unconfirmed until
run on hardware.

**Already known without hardware.** Restoring the pinned stack for `osx-arm64` from Windows
resolves all four packages with no version conflict, and both native payloads are present:
`runtimes/osx-arm64/native/libuiohook.dylib` (SharpHook) and `.../libminiaudio.dylib` (SoundFlow).
That does not prove either works on macOS, but it removes the "the library ships nothing for Apple
Silicon" failure mode from the deferred rows above, which was a real possibility.

**Consequence for the spec.** The `application-host` requirement *Verified platform stack* is
satisfied for win-x64 only, so this change does not archive and
`openspec/specs/application-host/` stays empty. Changes 2-5 must read this change's `specs/` folder
directly rather than the synced location.

**What this does and does not block.** The skeleton alone unblocks `add-settings-store`,
`add-file-logging` and `add-gemini-transcription` — none of them depends on a spike outcome. The
deferred rows gate `add-audio-pipeline` (S2), `add-global-hotkey` and `add-text-output` (S1), and
`add-tray-icon` (S3).

**Trigger to revisit.** If work reaches `add-tray-icon` (change 9) with the macOS column still
deferred, the fused change has become a real blocker and should be split at that point rather than
left open indefinitely.

### macOS spike results

Run from `spikes/Pisum.Whisper.Spikes` on an Apple M4, macOS 26.6.2 — `combined`, `hook`, `paste`,
`audio`, `tray`. `opus` was not re-run: S4 is pure computation with no platform surface, already
covered by the win-x64 result.

**First-run gotcha, not a code defect: Accessibility must be granted before anything runs.** The
`combined` spike's first-ever run hung indefinitely after `hook running alongside Avalonia: true` with
zero CPU and zero TCC log activity — not a permission dialog, a silent block. macOS never prompted;
the grant had to be added by hand under System Settings → Privacy & Security → Accessibility, for
**Rider** (the terminal's parent process), not for `dotnet` or the spike binary itself. Once granted,
`combined` passed immediately. Document this as a precondition for whoever runs these next.

**S1 — co-existence with Avalonia, PASS.** Once Accessibility was granted, `combined` matched the
Windows result exactly in shape: 9 presses, 9 releases, 6 icon updates via `Dispatcher.UIThread.Post`,
no contention. This resolves the project's highest-severity open question — see Open Questions below.

**S1 — both key edges, PASS after a pacing fix.** The first `hook` run reported both edges (3 presses,
3 releases, Space UP correctly carrying `LeftShift, LeftCtrl`) but the built-in verdict still read
FAIL: Space **DOWN** carried `mask=SimulatedEvent` only, missing both modifiers, even though each
modifier's own DOWN event showed the correct individual flag. Root cause: `EventSimulator` posting
three `SimulateKeyPress` calls back-to-back outruns macOS folding the earlier keys into the modifier
flags, so the last key's DOWN can arrive before the OS has caught up. Inserting a 30 ms delay after
every `SimulateKeyPress`/`SimulateKeyRelease` call fixed it completely — Space DOWN then carried
`LeftShift, LeftCtrl, SimulatedEvent` as expected. **`HookSpike.cs` was changed to pace its simulated
edges by 30 ms; any other macOS code driving `EventSimulator` through a modifier combo should do the
same.**

**S1b — paste into a foreign application, FAIL.** Even with the pacing fix applied and Accessibility
granted, the round-trip (token → clipboard → simulated Cmd+V into TextEdit → sentinel → simulated
Cmd+A/Cmd+C → read back) never recovers the token; the read-back is always exactly the sentinel that
was set moments before, meaning TextEdit's Select-All/Copy never fired at all — not merely that Paste
failed. TextEdit was confirmed frontmost with no dialog present (visual check). One additional finding
surfaced along the way: the very first run after each `dotnet build` shows the hook observing **zero**
key events at all (not just a failed paste — total silence), while the second run of the same,
unrebuilt binary reliably observes all 9. That points at task 1.4: an unsigned binary's TCC grant does
not appear to survive a rebuild, and there is no visible re-prompt when it silently reverts — a
`macOS spike results` finding in its own right (see the new 1.4 row in the matrix above). Root cause
of the paste failure itself is unresolved; **adopt the documented fallback** (clipboard-only output
with a manual-paste message) for macOS rather than depending on `EventSimulator` paste.

**S2 — capture, PASS (upgrades the Windows PARTIAL).** The MacBook's default microphone natively
supports 44100/48000/88200/96000 Hz, all mono — the first device seen in this project with a
non-48 kHz native rate, closing task 1.5a. Both the 48 kHz target and a forced off-native 16 kHz
request were honoured accurately (100.7% of expected samples each), unlike the Windows run's 93.9%
resampled shortfall. `add-audio-pipeline` can rely on miniaudio's resampling on this evidence, though
the Windows PARTIAL for the same rate stands as its own data point until re-measured there.

**S3 — tray icon, PASS; template image support still open.** Icon shown, tooltip visible on hover,
3-item native menu, 8 runtime image swaps — all confirmed by direct observation, matching the Windows
result. What remains open: whether Avalonia's `TrayIcon` flags the `NSImage` as a template (so macOS
auto-tints it for light/dark menu bars) was only exercised under `theme variant: Light`; no dark-mode
comparison was run, so this is unconfirmed rather than resolved. `add-tray-icon` should re-run `tray`
under both appearance modes before relying on template flagging.

**3.3 — no Dock icon, PASS.** `combined` sets `MacOSPlatformOptions { ShowInDock = false }`; confirmed
by direct observation that no Dock icon appears. (`tray` does not set this option and was not used for
this check, since a Dock icon there would be expected, not informative.)

### Task 1.4 re-test — 2026-09-02 (issue #31)

Re-run on the same Apple M4 that produced the FAIL above, macOS 26.6.2 unchanged between sessions.
From `spikes/Pisum.Whisper.Spikes`: `dotnet build --no-incremental` (forces a fresh ad-hoc signature —
confirmed via `codesign -dv`, `flags=0x2(adhoc)`, `TeamIdentifier=not set` before and after every
rebuild), then the built apphost executed directly. Three rebuild-then-run cycles in a row.

**All three passed on the first run after rebuild** — 3/3 press and release events, correct modifier
mask, no hang, no zero-event first run. None of the FAIL symptoms recorded above reproduced.

**Root cause: the grant was never the spike binary's.** `sqlite3` against the system TCC database
shows `com.jetbrains.rider` holding `2` (allowed) for both `kTCCServiceAccessibility` and
`kTCCServiceListenEvent` (Input Monitoring); no row exists for `Pisum.Whisper.Spikes` at all. Every
command in this session runs through the ancestry `rider → zsh → claude → zsh`, so macOS attributes
each TCC check to Rider as the responsible process — the same "for Rider (the terminal's parent
process)" behaviour already recorded above for the `combined` spike, generalised: it also covers Input
Monitoring, and it also covers a rebuild, because Rider's own code identity is what gets checked and
Rider's identity never changes. This does not falsify the original FAIL — that result presumably came
from a run outside Rider's process tree, which this session cannot reproduce, since every process it
spawns inherits Rider's ancestry.

**Consequence.** Spike/dev iteration through Rider's integrated terminal — the actual day-to-day
workflow in this repository — already survives rebuilds with no signing-identity work, riding on
Rider's own persistent grant. A stable development signing identity is therefore **not established as
part of this task**: it would only matter for a binary run outside Rider's ancestry (unverified either
way) or for the packaged `Pisum.Whisper.App.app` a real user eventually launches standalone, which
belongs to change 12 (`add-packaging-ci`), not here.

## Open Questions

- **Is `Concentus.Oggfile` usable with Concentus 2.x, or is a hand-rolled Ogg muxer required?**
  **Resolved — no spike needed.** `Concentus.Oggfile` 1.0.7 declares a dependency on
  `Concentus >= 2.2.2` and ships a `net8.0` target group, so it compiles against the pinned version
  and runs on `net10.0`. The plan's assumption of a hand-rolled muxer was pessimistic. S4 narrows
  from "can we write an Ogg muxer" to "does this one emit a correct OpusHead, pre-skip and granule
  positions", and `add-audio-pipeline` should be re-scoped before it is designed.

- **Does SharpHook's global hook require the main thread on macOS, and does that co-exist with
  Avalonia's run loop?** **Resolved — confirmed on Apple M4 hardware (macOS 26.6.2).** The
  highest-severity unknown in the project, closed: the desk-researched expectation (task 1.9) held —
  SharpHook's hook runs on its own background thread with its own `CFRunLoop`, independent of
  Avalonia's main-thread loop. `combined` passed with the same shape as Windows: 9 presses, 9
  releases, 6 icon updates via `Dispatcher.UIThread.Post`, no contention. See macOS spike results
  above. One precondition worth carrying forward: Accessibility must be granted to the process (or its
  terminal parent) before this works at all, and macOS does not always prompt for it.

- **Does Avalonia's `TrayIcon` expose NSImage template flagging, or must light and dark variants be
  shipped and selected manually?** **Still open.** The icon renders, the tooltip shows, and runtime
  swaps work, but that was only exercised under `theme variant: Light`; whether the icon is a true
  template image that macOS auto-tints for dark menu bars was not tested. `add-tray-icon` should run
  `tray` under both appearance modes before designing around an assumption either way.
