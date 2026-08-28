# Pisum Whisper — change sequence

Re-creation of `W:\github-pisum-transcript` (Tauri 2 + Svelte 5) as a .NET 10 application for
Windows x64 and macOS Apple Silicon. Cloud-only, Gemini-only: local Whisper inference is out of
scope despite the repository name.

Target pipeline:

```
global hotkey (hold or toggle) -> mic capture -> Opus/WAV encode
  -> Gemini upload with the active preset's system prompt
  -> clipboard + synthetic Ctrl+V / Cmd+V at the cursor
```

plus a tray icon with idle and recording states, a settings window, rolling file logging, and autostart.

## Dependency graph

```
 1 bootstrap-solution
    |-- 2 add-settings-store
    |      |-- 3 add-file-logging
    |      +-- 6 add-global-hotkey
    |-- 4 add-audio-pipeline ---- 5 add-gemini-transcription   (also needs 2)
    +-- 7 add-text-output
                        |
            8 add-dictation-pipeline      needs 4, 5, 6, 7   <-- first end-to-end dictation
                        |
            9 add-tray-icon               needs 8
                        |
           10 add-settings-window         needs 2, 3, 5, 6, 9
                        |
           11 add-system-integration      needs 2, 8, 10
                        |
           12 add-packaging-ci            needs all
```

After #1, changes **2 / 4 / 7** are independent and may run in parallel; after #2, **3 / 6** likewise.
Everything from #8 onward is strictly sequential.

## Changes

| # | Change | Capabilities | Depends on |
|---|---|---|---|
| 1 | `bootstrap-solution` | `application-host` | — |
| 2 | `add-settings-store` | `settings-persistence` | 1 |
| 3 | `add-file-logging` | `file-logging` | 2 |
| 4 | `add-audio-pipeline` | `audio-capture`, `audio-encoding` | 1 |
| 5 | `add-gemini-transcription` | `gemini-transcription` | 2, 4 |
| 6 | `add-global-hotkey` | `global-hotkey` | 1, 2 |
| 7 | `add-text-output` | `text-output` | 1 |
| 8 | `add-dictation-pipeline` | `dictation-pipeline` | 4, 5, 6, 7 |
| 9 | `add-tray-icon` | `tray-icon` | 8 |
| 10 | `add-settings-window` | `settings-window` | 2, 3, 5, 6, 9 |
| 11 | `add-system-integration` | `notifications`, `autostart` | 2, 8, 10 |
| 12 | `add-packaging-ci` | `packaging` | all |

## Artifact status

Changes **1-4** are implemented and archived under `openspec/changes/archive/`, with their
`application-host`, `settings-persistence`, `file-logging`, `audio-capture` and `audio-encoding`
specs synced into `openspec/specs/`. Change **6** has `design.md`, `specs` and `tasks.md` written but
is not yet implemented. Changes **5, 7-12** have `proposal.md` only; their `specs`, `design` and
`tasks` are written when their turn comes.

This is deliberate. The four spikes in change 1 can invalidate design decisions downstream — if
SharpHook cannot report key release, or miniaudio cannot resample, the affected designs change rather
than the code. Writing detailed task breakdowns for change 12 today would produce artifacts that are
stale before anyone reads them.

## Standing decisions

Recorded so they are not repeatedly re-litigated, and not mistaken for oversights:

- **Kept from the reference:** API keys stored in plaintext, and settings living at the
  home-directory root (`~/.pisum-whisper.json`) rather than the platform config directory.
- **Fixed relative to the reference:** the user's prior clipboard contents are restored after
  pasting; captured audio is buffered in mono chunks rather than one unbounded list locked inside
  the audio callback.
- **Diverged from the reference:** the two built-in preset prompts. They began as verbatim copies
  of the reference's German strings and were rewritten as English instructions in change 2;
  `BuiltinPresets.cs` is now their source of truth and they are deliberately not re-synced from
  `config/presets.rs`.
- **Out of scope:** local Whisper inference, OpenAI as a provider, input device selection,
  localization, auto-update.

The reference repository is the behavioural specification — wire formats, the recording state machine
and its timing constants, the settings schema, and the error taxonomy all come from it. It is not a
.NET project, so nothing is copied; it is read and re-expressed. The built-in preset prompts noted
above are the one deliberate exception.
