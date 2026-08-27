## Why

The repository is an empty scaffold: `Pisum.Whisper.slnx` contains no projects, so nothing can be
built, run or tested yet. Separately, three third-party libraries carry the riskiest behaviour in the
whole product — global key **release** events, cross-platform microphone capture, and a macOS
menu-bar icon. If any of them does not deliver, the architecture changes rather than the code. Both
problems are cheapest to solve now, before anything is built on top.

## What Changes

- Add `Directory.Build.props` (net10.0, nullable, implicit usings, warnings as errors) and
  `Directory.Packages.props` (central package management).
- Add four projects to `Pisum.Whisper.slnx`: `Core`, `Platform`, `App`, `Core.Tests`.
- Stand up the composition root: generic host + `Microsoft.Extensions.DependencyInjection`, and an
  Avalonia `AppBuilder` configured with `MacOSPlatformOptions { ShowInDock = false }` so the app is
  menu-bar-only from the first run.
- Run four de-risking spikes on **both** win-x64 and osx-arm64 and record the outcome:
  - **S1 SharpHook** — key down *and* up globally; threading and macOS run-loop requirements;
    `EventSimulator` producing a paste target apps accept.
  - **S2 SoundFlow** — open a capture device at 48 kHz mono f32; does miniaudio convert from the
    device's native rate?
  - **S3 Avalonia 12.1 tray** — runtime icon swap, tooltip on `NSStatusItem`, template images.
  - **S4 Concentus + Ogg** — encode and mux a file that plays back.
- Replace the placeholder "Status" section in `CLAUDE.md` with real build/test commands.

## Capabilities

### New Capabilities
- `application-host`: the app starts as a tray-only process, resolves its services from a container, and shuts down cleanly.

### Modified Capabilities
_None — this is the first change._

## Impact

Everything downstream depends on this. Spike outcomes are binding: a failed S1 replaces SharpHook
with hand-written `WH_KEYBOARD_LL` + `CGEventTap` interop; a failed S2 replaces SoundFlow with
PortAudioSharp2 or a NAudio/CoreAudio split. Later proposals are written against the stack this
change proves, so they must not be finalised until it lands.

## Non-goals

- No dictation behaviour, no hotkey handling, no audio, no Gemini calls.
- No settings window — the tray menu can be a stub.
- No packaging or installers.
