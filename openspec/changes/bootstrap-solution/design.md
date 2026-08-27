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

This change lands Windows-verified and remains open. No Apple Silicon hardware is available, so every
`osx-arm64` cell below is **deferred, not failed** — the distinction matters: a deferred row carries
no evidence either way, whereas a failed row triggers its fallback immediately.

| Spike | What must be demonstrated | win-x64 | osx-arm64 | Fallback if it fails |
|---|---|---|---|---|
| S1 | Global hook reports key press **and** release | **PASS** | deferred | Hand-written `WH_KEYBOARD_LL` / `CGEventTap` interop |
| S1 | Hook co-exists with Avalonia's run loop | **PASS** | deferred — expectation recorded by task 1.9 | Threading redesign; highest severity in the project |
| S1 | `EventSimulator` paste accepted by a foreign app | **PASS** | deferred | Clipboard-only output with a manual-paste message |
| S2 | Capture opens at 48 kHz mono f32, resampling from the device's native rate | **PARTIAL** | deferred | `NAudio.Core`'s managed `WdlResampler` behind `IAudioCapture` |
| S3 | Tray icon visible; image swappable at runtime | **PASS** | deferred | Platform-specific tray implementation in `Pisum.Whisper.Platform` |
| S3 | Tooltip on `NSStatusItem`; template image support | n/a | deferred | Active preset name moves into the menu itself |
| S4 | Encoded `.opus` decodes back to the same duration | **PASS** | covered by win-x64 — pure computation, no platform surface | Hand-rolled Ogg muxer, as originally planned |
| S4 | Accessibility grant survives a rebuild under a stable signing identity

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

This machine has only one input device and it is natively 48 kHz, so **the production direction
(44.1 kHz device → 48 kHz request) could not be tested at all.** Consequence for `add-audio-pipeline`:
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

## Open Questions

- **Is `Concentus.Oggfile` usable with Concentus 2.x, or is a hand-rolled Ogg muxer required?**
  **Resolved — no spike needed.** `Concentus.Oggfile` 1.0.7 declares a dependency on
  `Concentus >= 2.2.2` and ships a `net8.0` target group, so it compiles against the pinned version
  and runs on `net10.0`. The plan's assumption of a hand-rolled muxer was pessimistic. S4 narrows
  from "can we write an Ogg muxer" to "does this one emit a correct OpusHead, pre-skip and granule
  positions", and `add-audio-pipeline` should be re-scoped before it is designed.

- **Does SharpHook's global hook require the main thread on macOS, and does that co-exist with
  Avalonia's run loop?** **Unconfirmed — no hardware.** The highest-severity unknown in the project:
  a conflict here is not a library swap but a threading redesign. Task 1.9 reduces it by
  desk-research, and that research is done: SharpHook documents `CFRunLoopGetCurrent` as being
  called on whichever thread runs the hook, and `RunAsync` as using a separate thread, so the
  expectation is co-existence. The `combined` spike proves that composition works on Windows and is
  the harness to run first on a Mac. Still unconfirmed on hardware; see Windows spike results above.

- **Does Avalonia's `TrayIcon` expose NSImage template flagging, or must light and dark variants be
  shipped and selected manually?** **Deferred — no hardware.** S3 answers it, and `add-tray-icon`
  depends on the answer; that change should not be designed until then.
