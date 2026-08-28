# Pisum Whisper

Hotkey-driven, tray-resident dictation. Hold a global hotkey to record speech, release to transcribe
it with Google Gemini, and the transcript is pasted at the cursor position in whatever application
you were already typing in.

Targets **Windows x64** and **macOS Apple Silicon**. Cloud-only and Gemini-only — despite the name,
local Whisper inference is out of scope.

## Status

Under construction, and **not yet usable**. Work is sequenced as twelve ordered changes in
[`openspec/ROADMAP.md`](openspec/ROADMAP.md); changes 1, 2, 3, 4 and 6 of 12 have landed, which means
the solution builds and starts as a tray-only process with a Quit menu, reads its settings from
`~/.pisum-whisper.json`, creating that file on first run and repairing it when it has gone stale,
writes a rolling log to `~/.pisum-whisper/logs/`, can capture microphone audio and encode it to Opus
or WAV, and observes the configured global hotkey — both edges of it, withheld from whatever
application has focus, and re-bindable without a restart.

Nothing yet connects the hotkey to the audio pipeline: there is no recording, no transcription and
no settings UI yet.

macOS is **partly verified**. Change 1's spikes were re-run on an Apple M4 (macOS 26.6.2) under
[issue #15](https://github.com/mschnecke/pisum-whisper/issues/15), now closed: the global hook, its
co-existence with Avalonia's run loop, capture and the menu-bar icon all pass. Two cells came back
**FAIL** — the synthetic paste is not accepted by a foreign application, and the Accessibility grant
does not survive a rebuild of an unsigned binary — and each has a documented fallback. The app
itself has still never been run end to end on macOS. See the *Platform verification* matrix in
[`design.md`](openspec/changes/archive/2026-08-27-bootstrap-solution/design.md) for the detail.

## Prerequisites

- **.NET SDK `10.0.400`** — pinned in `global.json`, so a different patch level will refuse to build.
  Get it from <https://dotnet.microsoft.com/download/dotnet/10.0>.
- A working microphone.
- A **Google Gemini API key**, once transcription lands ([aistudio.google.com](https://aistudio.google.com/app/apikey)).

No other tooling is required. All packages come from nuget.org; the repository ships a `NuGet.config`
that pins that source, so a machine configured for private feeds still restores correctly.

## Build and run

```bash
git clone git@github.com:mschnecke/pisum-whisper.git
cd pisum-whisper
dotnet build Pisum.Whisper.slnx
dotnet test Pisum.Whisper.slnx
dotnet run --project src/Pisum.Whisper.App
```

The app has **no window**. It starts as a tray icon — on Windows 11 a newly registered icon is
placed in the *hidden* overflow, so click the `^` chevron in the notification area to see it. Right
click it for Quit. `dotnet run` will not return until you quit, which is expected for a tray process
rather than a hang.

To build for a specific runtime, name the project rather than the solution (a `.slnx` cannot be
built for a single RID):

```bash
dotnet build src/Pisum.Whisper.App -r win-x64
dotnet build src/Pisum.Whisper.App -r osx-arm64
```

## Logs

Diagnostics go to `~/.pisum-whisper/logs/pisum-whisper.log`. The file rolls at 1 MB, at most ten are
kept, and any file older than seven days is deleted at startup. Three keys in `~/.pisum-whisper.json`
control that:

| Key | Default | |
|---|---|---|
| `logLevel` | `"info"` | one of `trace`, `debug`, `info`, `warn`, `error`; a change takes effect immediately, without a restart |
| `logMaxFileSizeMb` | `1` | size at which the active file rolls |
| `logRetentionDays` | `7` | age past which a file is swept at startup |

A debug build also echoes to the terminal it was launched from; a release build writes to the file
only.

## Permissions

**macOS** will require two grants, neither of which can be pre-approved:

- **Accessibility** — `System Settings → Privacy & Security → Accessibility`. Needed *twice over*:
  the global hotkey installs a `CGEventTap`, and pasting synthesises Cmd+V. Without it the app runs
  but never sees the hotkey and never pastes. macOS does not always say so: an ungranted tap can
  block silently rather than failing, so the app waits five seconds, logs that the hotkey is not
  being observed and starts anyway. Grant it and **relaunch** — macOS does not reliably hand a new
  grant to a process that is already running, so this costs one restart on first run.
- **Microphone** — `System Settings → Privacy & Security → Microphone`, prompted on first recording.

A caution for anyone developing on macOS: the Accessibility grant is bound to the binary's **code
signature**, so an unsigned binary re-prompts on every rebuild. Issue #15 confirmed this on
hardware, and it is the matrix's row 1.4 `FAIL`. Establishing a stable development signing identity
is the recorded fallback and is worth doing before iterating on the hotkey.

**Windows** needs no grant for the hotkey or for pasting. Two limits are worth knowing: microphone
access is governed by `Settings → Privacy & security → Microphone`, and a non-elevated process
cannot paste into an elevated window — in that case the transcript is still on the clipboard and the
app says so, which is expected behaviour rather than a defect.

## Repository layout

| Path | |
|---|---|
| `src/Pisum.Whisper.Core` | domain and orchestration; no platform or UI dependencies |
| `src/Pisum.Whisper.Platform` | the OS-specific surface |
| `src/Pisum.Whisper.App` | Avalonia tray shell and composition root |
| `tests/Pisum.Whisper.Core.Tests` | MSTest, FakeItEasy, Shouldly |
| `spikes/` | throwaway de-risking spikes, outside the solution |
| `openspec/` | the spec-driven change workflow that drives this repository |

## Contributing

Work is spec-driven: every change starts as a proposal under `openspec/changes/` before any code is
written. Read [`openspec/ROADMAP.md`](openspec/ROADMAP.md) first — it carries the dependency graph
and the standing decisions, including what is deliberately out of scope.

## License

MIT — see [LICENSE](LICENSE).
