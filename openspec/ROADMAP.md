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

plus a tray icon with idle, recording and transcribing states, a settings window, rolling file
logging, and autostart.

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
                        |                     and migrate-tests-to-xunit-v3 (off-sequence)
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

## Off-sequence changes

Work that is real and tracked but carries no number, because it adds no capability and does not sit
on the pipeline the twelve rows above sequence.

| Change | What it is | Blocks |
|---|---|---|
| `migrate-tests-to-xunit-v3` | MSTest to xUnit v3 across both test projects; `dotnet test` moves to Microsoft.Testing.Platform | 10 |

`add-settings-window` depends on it: Avalonia ships first-party headless test integration for xUnit
and NUnit only — there is no `Avalonia.Headless.MSTest` and there never has been — so change 10
cannot write a headless test for its window until this is in place. That is the whole reason the
migration ran before change 9 rather than whenever it became convenient.


## Artifact status

Changes **1-11** are implemented and archived under `openspec/changes/archive/`, with their
`application-host`, `settings-persistence`, `file-logging`, `audio-capture`, `audio-encoding`,
`global-hotkey`, `gemini-transcription`, `text-output`, `dictation-pipeline`, `tray-icon`,
`settings-window`, `notifications` and `autostart` specs synced into `openspec/specs/`. Change **12**
has `proposal.md` only; its `specs`, `design` and `tasks` are written when its turn comes.

**Seven archived changes carry unchecked tasks, twenty in total, and they are not all macOS.** This
section previously named changes 8 to 11 and called that "a departure from how 1-7 were closed".
Both halves were wrong: changes 1 and 7 are in the same state, so there was no departure — only the
recording of it changed.

| Change | Open | Needs |
|---|---|---|
| 1 `bootstrap-solution` | 1.4, 1.5a | Apple Silicon; and a 44.1 kHz capture device, which is not a platform question |
| 7 `add-text-output` | 3.3, 3.5, 3.6, 5.4 | win-x64 for 3.3, Apple Silicon for the rest. The code for 3.3, 3.5 and 3.6 is in the tree — only their manual checks are owed |
| 8 `add-dictation-pipeline` | 6.1, 6.3, 6.4 | win-x64 for the end-to-end run and the budget measurement; Apple Silicon for 6.4 |
| 9 `add-tray-icon` | 4.1, 4.2, 4.3 | win-x64 for 4.1; Apple Silicon for the osx-arm64 pass and the `spikes -- tray` run under both appearance modes |
| 10 `add-settings-window` | 6.2, 6.3, 6.4 | Apple Silicon for 6.2; win-x64 for 6.3 and 6.4 |
| 11 `add-system-integration` | 7.1, 7.2, 7.3, 7.4 | win-x64 for 7.1; Apple Silicon for 7.2 and the `spikes -- toast` re-run; 7.4 is a headless test plus a glance during 7.1 |
| `migrate-tests-to-xunit-v3` | 5.4 | win-x64 — confirm Rider still discovers the tests |

**Eight of the twenty need nothing but the Windows machine this is developed on**, and a ninth is
7.4's headless test. Calling the whole of it "one Apple Silicon sitting" is what has kept those
sitting. Ten do need Apple Silicon and should be done in one pass; one needs a 44.1 kHz input device
and is not about either platform.

**An unchecked box does not mean the work never ran, and this section is where that was learned.**
Change 1's macOS tasks did run, under issue #15 on an Apple M4, and the results have been in that
change's `design.md` matrix since 2026-08-28 — but the boxes went unticked and were counted as
outstanding twice before being reconciled on 2026-09-02. Two came back `FAIL`: 1.3b's documented
fallback is what change 7 shipped, while 1.4's fallback is 1.4 itself, which is why it alone stays
open. Every one of these changes is archived regardless, so **an archived change here does not
certify that its capability was verified on hardware** — and a ticked box certifies that a check ran,
not that it passed.

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
- **Off-sequence work gets a section, not a number.** `migrate-tests-to-xunit-v3` adds no capability,
  so numbering it would either renumber twelve rows or invent an "8.5"; leaving it out entirely — the
  precedent set by tracking change 1's macOS verification as issue #15 — would hide the fact that
  change 10 now depends on it. The *Off-sequence changes* section above answers both, and the next
  piece of numberless work belongs there too rather than re-opening this.

The reference repository is the behavioural specification — wire formats, the recording state machine
and its timing constants, the settings schema, and the error taxonomy all come from it. It is not a
.NET project, so nothing is copied; it is read and re-expressed. The built-in preset prompts noted
above are the one deliberate exception.
