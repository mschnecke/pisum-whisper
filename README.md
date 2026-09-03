# Pisum Whisper

Hotkey-driven, tray-resident dictation. Hold a global hotkey to record speech, release to transcribe
it with Google Gemini, and the transcript is pasted at the cursor position in whatever application
you were already typing in.

Targets **Windows x64** and **macOS Apple Silicon**. Cloud-only and Gemini-only — despite the name,
local Whisper inference is out of scope.

## Status

Under construction, but installable. Work is sequenced as twelve ordered changes in
[`openspec/ROADMAP.md`](openspec/ROADMAP.md); **all twelve have now landed in code**, which means the
solution builds and starts as a tray-only process, reads its settings from
`~/.pisum-whisper.json`, creating that file on first run and repairing it when it has gone stale,
writes a rolling log to `~/.pisum-whisper/logs/`, can capture microphone audio and encode it to Opus
or WAV, can transcribe that audio with Gemini — retried, with a round-robin key pool and categorised
failures — observes the configured global hotkey, both edges of it, withheld from whatever
application has focus and re-bindable without a restart, can deliver a transcript at the cursor
through the clipboard and a synthetic paste, and wires all of that into one dictation: hold the
hotkey to record, release to transcribe and paste, with the tray icon reporting which of idle,
recording and transcribing it is in. On top of that it has a settings window for every one of those
settings, tells you when a dictation fails, can register itself to start at login, and — change 12 —
ships as an installer for each platform, built and released by continuous integration.

Off that numbered sequence, it also reports the failures that happen *before* any of the above
exists: a failure that stops it starting reaches you as a native dialog rather than a silent exit,
and the two conditions that let it start but leave it unable to work — a log directory it could not
create, and a hotkey it is not being allowed to observe — are reported once there is a tray icon to
report them on. A follow-up fix ([issue #34](https://github.com/mschnecke/pisum-whisper/issues/34))
corrected that dialog's title: it used to match on the exception's *type*, so an unrelated I/O
failure — a missing tray icon asset — could be shown as a settings-file error; it is now matched by
where the failure actually happened. A second follow-up ([issue #37](https://github.com/mschnecke/pisum-whisper/issues/37))
closed the matching gap once the window is already open: a save that failed to reach disk — full,
permission denied, a network drive gone — used to look like it had worked, because nothing was
awaiting the write. It is now reported on screen, and a failed preset save reverts to what is actually
saved rather than continuing to show the edit.

One thing still qualifies all of it. Dictation has been run end to end by hand — on win-x64 in
both hold and toggle mode, and on macOS too, as a byproduct of changes 9 and 11's own verification
runs — but **nine** archived changes were each left with a piece of their manual verification open,
and change 12 adds five checks of its own that need a clean machine of each kind: an install from
the `.msi` and from the `.pkg`, whether the Accessibility grant survives an update, autostart from an
installed build, and a startup dialog from one. Those five are tracked by
[#52](https://github.com/mschnecke/pisum-whisper/issues/52), and `ready-the-suite-for-ci`'s three
win-x64 checks by [#50](https://github.com/mschnecke/pisum-whisper/issues/50); the rest are recorded
only in `openspec/ROADMAP.md`'s *Artifact status* table. The capabilities are complete in code and
covered by tests; what remains is measurement and a handful of by-hand checks, not a demonstration
that the pipeline works at all — though the startup dialogs themselves have so far only been drawn
on Windows, never on macOS.

macOS is **more thoroughly verified now**. Change 1's spikes were re-run on an Apple M4 (macOS
26.6.2) under [issue #15](https://github.com/mschnecke/pisum-whisper/issues/15), now closed: the
global hook, its co-existence with Avalonia's run loop, capture and the menu-bar icon all pass.
[Issue #31](https://github.com/mschnecke/pisum-whisper/issues/31) carried the same sitting forward
for changes 1, 7, 8, 9, 10 and 11 on 2026-09-02, and resolved both of the original spike's two
**FAIL** cells: the synthetic paste a foreign application would not accept turns out not to
reproduce through the shipped `TextOutput`/`MacOsClipboard`/`MacOsPasteProbe` path, which pastes
correctly and repeatably, and the Accessibility grant does survive a rebuild when the process is
launched through Rider. Whether it survives an *update* to an installed build is change 12's open
question and is unanswered. Issue #31 was
closed on 2026-09-02 with change 8's refused-microphone case still outstanding — its one
verification attempt was abandoned rather than completed. See the *Platform
verification* matrix in
[`design.md`](openspec/changes/archive/2026-08-27-bootstrap-solution/design.md) for the detail.


## Install

Both installers are **unsigned** — no Apple Developer ID, no Windows code-signing certificate. That
is a deliberate decision, not an omission, and the two places it costs you something are called out
below. Nothing else is needed: each installer carries its own .NET runtime, so a machine with no
.NET installed is fine.

### macOS (Apple Silicon, macOS 12 or later)

```bash
brew tap mschnecke/pisum-whisper
brew install --cask pisum-whisper
```

Or download `Pisum.Whisper_<version>_osx-arm64.pkg` from the
[latest release](https://github.com/mschnecke/pisum-whisper/releases) and open it. Either route runs
the same installer, which puts **Pisum Whisper.app** in `/Applications` and clears the quarantine
attribute, so it opens from Finder with no "unidentified developer" warning and no right-click-Open
detour.

Then, once, before it can do anything:

1. Launch it. It is a menu-bar application — look in the menu bar, not the Dock.
2. Open **System Settings → Privacy & Security → Accessibility** and enable **Pisum Whisper**. It
   needs this twice over: the global hotkey installs a system-wide event tap, and pasting the
   transcript synthesises Cmd+V.
3. **Quit and relaunch it.** macOS does not reliably hand a new grant to a running process.
4. Enter a Gemini API key on the settings window's **Providers** tab, and dictate. The microphone is
   prompted for separately, the first time you record.

**You will have to grant Accessibility again after every update.** Without a Developer ID signature
macOS has no stable identity to recognise the new build by, so it treats each version as a different
application. This is the price of shipping unsigned and it is the one most likely to annoy you.

### Windows (x64, Windows 10 or later)

```powershell
choco install pisum-whisper --source https://www.myget.org/F/mschnecke/api/v3/index.json
```

Or download `Pisum.Whisper_<version>_win-x64.msi` from the
[latest release](https://github.com/mschnecke/pisum-whisper/releases) and run it.

**SmartScreen will warn you** — "Windows protected your PC" — because the installer is unsigned and
has no download reputation. To continue: click **More info**, then **Run anyway**. There is no way
around this short of a code-signing certificate; if you would rather not, build from source with the
instructions below.

The installer is per-machine and asks for elevation. It adds a **Pisum Whisper** Start-menu shortcut
and an entry in *Apps & Features*. Launch it from the Start menu, then enter a Gemini API key on the
settings window's **Providers** tab. On Windows 11 a newly registered tray icon goes into the hidden
overflow, so click the `^` chevron in the notification area to find it. No permission grant is
needed for the hotkey or for pasting.

### Uninstalling

Uninstalling removes the application and nothing else: `~/.pisum-whisper.json` and
`~/.pisum-whisper/logs/` stay, because they hold the API keys and presets you entered and a
reinstall is not a request to discard them.

| | |
|---|---|
| Windows | *Apps & Features*, or `choco uninstall pisum-whisper` |
| macOS | `brew uninstall --cask pisum-whisper`, or drag `/Applications/Pisum Whisper.app` to the Trash |
| macOS, data as well | `brew uninstall --zap --cask pisum-whisper` — also removes the settings file, the logs and the launch agent |

## Prerequisites

To *use* it, once installed:

- A working microphone.
- A **Google Gemini API key** ([aistudio.google.com](https://aistudio.google.com/app/apikey)).

To *build* it:

- **.NET SDK `10.0.400`** — pinned in `global.json`, so a different patch level will refuse to build.
  Get it from <https://dotnet.microsoft.com/download/dotnet/10.0>.

No other tooling is required for a build or a test run. All packages come from nuget.org; the
repository ships a `NuGet.config` that pins that source, so a machine configured for private feeds
still restores correctly. Building an *installer* needs one tool more per platform — the WiX v6
.NET tool on Windows, and nothing but the Xcode Command Line Tools on macOS; see
[`packaging/README.md`](packaging/README.md).


## Build and run

```bash
git clone git@github.com:mschnecke/pisum-whisper.git
cd pisum-whisper
dotnet build Pisum.Whisper.slnx
dotnet test Pisum.Whisper.slnx
dotnet run --project src/Pisum.Whisper.App
```

The app is **tray-resident**: no taskbar button, no Dock icon, and no window until you ask for one.
On Windows 11 a newly registered tray icon is placed in the *hidden* overflow, so click the `^`
chevron in the notification area to see it. Hovering names the active preset; right clicking offers
Settings and Quit. The icon takes a different shape for each of idle, recording and transcribing, so
a glance says whether the application is listening, uploading, or waiting for the hotkey. `dotnet
run` will not return until you quit, which is expected for a tray process rather than a hang.

**On a first run it opens the settings window by itself** and shows a welcome notification, because
there is nothing it can transcribe until a Gemini key is entered and a tray icon filed into the
overflow is not a discoverable first step. Every run after that starts silently in the tray. Closing
the window hides it rather than quitting — the application keeps running and the hotkey keeps
working; Quit is on the tray menu.

The window has six tabs — Providers, Presets, Hotkey, Audio, Logging and General. Edits are saved as
you make them: there is no OK, Cancel or Apply, and they reach the running application without a
restart, including the hotkey binding and the log level. If a save cannot reach disk, you are told on
screen rather than the window quietly looking like it worked.

**If it cannot start at all, it says so in a dialog** naming what failed and where the log would be,
then exits non-zero. Four failures reach it; the two you could plausibly hit are a settings file that
is not valid JSON and one it cannot write, the other two — a missing tray asset and a service
container that fails validation — being developer-facing. A tray-only process has nowhere else to put
such a message: with no window and no console, exiting quietly is indistinguishable from never having
been launched, which matters most when it was launched from a login item and nobody was watching.

To build for a specific runtime, name the project rather than the solution (a `.slnx` cannot be
built for a single RID):

```bash
dotnet build src/Pisum.Whisper.App -r win-x64
dotnet build src/Pisum.Whisper.App -r osx-arm64
```

## Notifications and start at login

Two keys in `~/.pisum-whisper.json` control these, both editable on the settings window's **General**
tab:

| Key | Default | |
|---|---|---|
| `showTrayNotifications` | `true` | whether *status* messages are shown; failures are shown either way |
| `startWithSystem` | `true` | whether the application registers itself to start at login |

A dictation that fails is reported on screen rather than only in the log, because the window is
usually hidden and the tray icon reports a state and not a reason. **Turning notifications off does
not silence failures** — a rejected API key still has to reach you — it silences status messages such
as pressing the hotkey during a transcription, or a recording stopped at the maximum duration. The
same exemption covers a **degraded start** — a log directory that could not be created, or a hotkey
that is not being observed — because both leave the application looking exactly like one that is
doing nothing at all. A notification never takes focus, which matters because this application pastes
at the cursor in whatever you are typing in; it clears itself after a few seconds and is not
clickable. No transcript, API key or clipboard content ever appears in one.

Autostart is *reconciled* rather than toggled: the registration is compared with the setting at every
launch and whenever settings are saved, and written only when the two disagree — so an entry removed
by another tool comes back, and a hand-edited settings file is honoured. What gets written is a
per-user registration needing no elevation:

| | |
|---|---|
| Windows | a `Pisum Whisper` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| macOS | a LaunchAgent plist at `~/Library/LaunchAgents/net.pisum.whisper.plist` |

What gets registered is the path of the executable that is running: the build output under `dotnet
run`, and `/Applications/Pisum Whisper.app/Contents/MacOS/Pisum.Whisper.App` or the installed
`Pisum.Whisper.App.exe` for an installed build. If a registration is already there but names a
different executable — the build you had been running before you installed, say — it is rewritten on
the next launch rather than left pointing at the old path. A registration that cannot be written costs
you autostart and is logged; it never stops the application from starting.

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

If the directory cannot be created the application still starts — a dictation tool that refuses to
launch because it cannot write a log is worse than one that launches without one — and it now tells
you so on screen. That message has to be a notification rather than a log line for the obvious
reason: the one thing that could explain why there is no log is the one thing that cannot be written
to it.

## Permissions

**macOS** will require two grants, neither of which can be pre-approved:

- **Accessibility** — `System Settings → Privacy & Security → Accessibility`. Needed *twice over*:
  the global hotkey installs a `CGEventTap`, and pasting synthesises Cmd+V. Without it the app runs
  but never sees the hotkey and never pastes. macOS does not always say so: an ungranted tap can
  block silently rather than failing, so the app waits five seconds, then starts anyway and tells you
  on screen that keys are not being observed. On a first macOS launch that notification appears beside
  the welcome, which is the expected pair rather than a fault — the grant cannot be present yet.
  Access withdrawn *while* the app is running is reported the same way, whenever it happens. Grant it
  and **relaunch** — macOS does not reliably hand a new grant to a process that is already running,
  so this costs one restart on first run.
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
| `src/Pisum.Whisper.Platform` | the OS-specific surface; the clipboard, the paste probes, autostart and the native startup-failure dialog |
| `src/Pisum.Whisper.App` | Avalonia tray shell, settings window, notification toasts and composition root |
| `tests/Pisum.Whisper.Core.Tests` | xUnit v3, FakeItEasy, Shouldly |
| `tests/Pisum.Whisper.Platform.Tests` | native registration, and the manual clipboard round trip |
| `tests/Pisum.Whisper.App.Tests` | the settings window and toasts, on `Avalonia.Headless.XUnit` |
| `packaging/` | the installers: the icon, the WiX source, the macOS bundle and `.pkg` scripts, the Chocolatey package |
| `.github/workflows/` | `ci.yml` builds, tests and packages both platforms on every pull request; `release.yml` publishes on a `v*` tag |
| `spikes/` | throwaway de-risking spikes, outside the solution |
| `openspec/` | the spec-driven change workflow that drives this repository |

## Contributing

Work is spec-driven: every change starts as a proposal under `openspec/changes/` before any code is
written. Read [`openspec/ROADMAP.md`](openspec/ROADMAP.md) first — it carries the dependency graph
and the standing decisions, including what is deliberately out of scope.

## License

MIT — see [LICENSE](LICENSE).
