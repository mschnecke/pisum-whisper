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
Serilog and the test stack will drift otherwise.

**Avalonia configured menu-bar-only from the start**, through
`MacOSPlatformOptions { ShowInDock = false }`. The reference achieves this with an objc2 call to
`NSApplication.setActivationPolicy(Accessory)`; Avalonia exposes it directly, so no interop is needed.

**Warnings as errors, nullable enabled.** Cheap at four empty projects, expensive to introduce at
thirty populated ones.

**Spikes are throwaway and are not merged.** Their output is a decision recorded in this change, not
code. Writing them as production abstractions invites keeping a design that the spike disproved.

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

## Open Questions

- Does Avalonia's `TrayIcon` expose NSImage template flagging, or must light and dark variants be
  shipped and selected manually? S3 answers this and `add-tray-icon` depends on the answer.
- Does SharpHook's global hook require the main thread on macOS, and does that co-exist with
  Avalonia's run loop? S1 answers this.
- Is `Concentus.Oggfile` usable with Concentus 2.x, or is a hand-rolled Ogg muxer required? The plan
  assumes hand-rolled; S4 confirms.
