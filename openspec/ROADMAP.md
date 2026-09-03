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

Ordered by when each was proposed.

| Change | What it is | Blocks | Status |
|---|---|---|---|
| `migrate-tests-to-xunit-v3` | MSTest to xUnit v3 across both test projects; `dotnet test` moves to Microsoft.Testing.Platform | **10** | Archived 2026-09-01 |
| `report-startup-failures` | A native modal dialog for the four failures that prevent startup, and the notification transport for the two degraded conditions that reach nobody today | nothing | Archived 2026-09-02, closes #20 |
| `fix-startup-ioexception-mislabeling` | `StartupFailure.Describe` matched on exception *type*, so a missing tray asset's `FileNotFoundException` was reported as a settings error — telling the user the file holding their API keys was broken when it never was | nothing numbered; `surface-settings-save-failures` builds on it | Archived 2026-09-02, closes #34 |
| `settle-win-x64-verification-debt` | The eleven by-hand checks across seven archived changes that need nothing but the Windows machine this is developed on | nothing | Archived 2026-09-02, closes #30 |
| `surface-settings-save-failures` | A settings write that never reached disk became an unobserved task exception, so the window looked like the save had worked | nothing | Archived 2026-09-02, closes #37 |
| `ready-the-suite-for-ci` | Five write-failure tests whose `FileShare.None` arrangement only fails on Windows, and a latency test that gates itself instead of being filtered by CI, so the suite passes unattended on both platforms | **12** | Active, #49 |

`add-settings-window` depends on `migrate-tests-to-xunit-v3`: Avalonia ships first-party headless
test integration for xUnit and NUnit only — there is no `Avalonia.Headless.MSTest` and there never
has been — so change 10 cannot write a headless test for its window until it is in place. That is
the whole reason the migration ran before change 9 rather than whenever it became convenient.

`report-startup-failures` blocks nothing, and change 12 does not depend on it. It is off-sequence for
the same reason the migration is — it adds a capability to a pipeline that is already built rather
than a stage to the pipeline — and it exists because changes 9 and 11 each deferred
`HotkeyAvailability.Failed` to the next change along, and change 11's own *Risks* recorded that its
transport could not reach a failure raised before the dispatcher loop starts. It also closes issue
#20. It is dependent on change 11 for the degraded half only; the dialog half depends on nothing.

`ready-the-suite-for-ci` is the first off-sequence change that **blocks a numbered one**. Change 12
adds the first CI this repository has had, and on `main` today its macOS leg would be red before a
line of packaging is written: measured 2026-09-03, `dotnet test --filter-not-trait Category=Manual`
gives 625 selected, 615 passed, **5 failed**, 5 skipped. The five are `PresetsViewModelTests` and
`SettingsEditorTests` write-failure tests that hold the settings file open with `FileShare.None` —
which blocks `File.Move` on Windows and not on macOS, where `rename(2)` ignores the destination's
open descriptors. They were found by hand during issue #31's sitting, reported in PR #48's
description, and left for an owner that PR's archive no longer has. Change 12's task group 5 cannot
land green until this does. Tracked on issue #49.

**Three rows were missing until 2026-09-03, and the omission has a shape worth naming.** The table
listed the two changes that were *planned* as off-sequence work and none of the three that began as
a bug report — `fix-startup-ioexception-mislabeling` (#34), `settle-win-x64-verification-debt` (#30)
and `surface-settings-save-failures` (#37) were each proposed, implemented and archived without the
row being added. So a reader counting off-sequence changes here found two while the archive held
five. In `settle-win-x64-verification-debt`'s case this was not an oversight at all: **its own task
7.1 says "the *Off-sequence changes* table gains a row for this change", and that task is
unchecked** — the change was archived with eight open boxes, one of which was adding itself here.
The rest of 7.1 is still owed and is not done by this correction: the *Artifact status* table's
win-x64 entries, the three *Standing decisions* rules it specifies, and its `CLAUDE.md` and
`README.md` edits.


## Artifact status

Changes **1-11** are implemented and archived under `openspec/changes/archive/`, with their
`application-host`, `settings-persistence`, `file-logging`, `audio-capture`, `audio-encoding`,
`global-hotkey`, `gemini-transcription`, `text-output`, `dictation-pipeline`, `tray-icon`,
`settings-window`, `notifications` and `autostart` specs synced into `openspec/specs/`. Change **12**
has `proposal.md` only; its `specs`, `design` and `tasks` are written when its turn comes.
`report-startup-failures` is implemented and archived as well, on 2026-09-02, with its
`file-logging`, `global-hotkey` and new `startup-diagnostics` specs synced. **7.1 has since closed in
full, and 7.3 partly has** — the corrupt-settings reproduction that closed issue #20, three more
fatal-dialog reproductions run the same day under `settle-win-x64-verification-debt`, and 7.3's
interactive-launch half alongside them. What is left is 7.2's Apple Silicon pass and 7.3's
login-time half, so it still joins the table below rather than closing with it.

**Seven archived changes carry unchecked tasks, ten in total, and they are not all macOS.** This
section previously named changes 8 to 11 and called that "a departure from how 1-7 were closed".
Both halves were wrong: changes 1 and 7 are in the same state, so there was no departure — only the
recording of it changed. Change 9's row is gone as of 2026-09-02: issue #31 closed all three of its
open tasks, along with every other macOS task from changes 1 and 7 except the ones this table still
lists, in one sitting on an Apple M4.

| Change | Open | Needs |
|---|---|---|
| 1 `bootstrap-solution` | 1.5a | a 44.1 kHz capture device, which is not a platform question |
| 7 `add-text-output` | 3.3 | win-x64. The code is in the tree — only the manual check is owed |
| 8 `add-dictation-pipeline` | 6.3, 6.4 | win-x64 for the budget measurement; Apple Silicon for 6.4's refused-microphone case — its end-to-end half closed 2026-09-02, confirmed twice over by changes 9 and 11's own macOS runs |
| 10 `add-settings-window` | 6.4 | win-x64 |
| 11 `add-system-integration` | 7.1, 7.4 | win-x64 for 7.1; 7.4 is a headless test plus a glance during 7.1 |
| `migrate-tests-to-xunit-v3` | 5.4 | win-x64 — confirm Rider still discovers the tests |
| `report-startup-failures` | 7.2, 7.3 | Apple Silicon for 7.2; win-x64 for 7.3's login-time half. 7.1 (all four fatal-dialog reproductions, closing #20) and 7.3's interactive-launch half both ran 2026-09-02. The `osascript` dialog has never been drawn |

**None of the ten is tracked by an issue.** #30 (win-x64) and #31 (Apple Silicon) were both closed
on 2026-09-02 with work still open — #30 while six of its eleven checks had not run, #31 with change
8's refused-microphone case abandoned rather than completed. This table is the only surviving
record, which is the state the *Standing decisions* rule on moving open by-hand tasks to a tracking
issue exists to prevent. Reopening the two, or opening one successor, is an open decision.

**Six of the ten need nothing but the Windows machine this is developed on**, and a seventh is
11's 7.4 headless test. Two do need Apple Silicon and should be done in one pass — issue #31's
successor, once hardware is available again; one needs a 44.1 kHz input device and is not about
either platform.

**An unchecked box does not mean the work never ran, and this section is where that was learned.**
Change 1's macOS tasks did run, under issue #15 on an Apple M4, and the results have been in that
change's `design.md` matrix since 2026-08-28 — but the boxes went unticked and were counted as
outstanding twice before being reconciled on 2026-09-02. Two came back `FAIL`: 1.3b's documented
fallback is what change 7 shipped, while 1.4 came back `FAIL` with no fallback but itself — the task
of establishing a signing identity. Re-run under issue #31, also on 2026-09-02, it closed the other
way: the grant that matters belongs to Rider, the terminal every command in that session inherited,
not to the binary's own signature, so no signing identity was ever built and none was needed inside
that ancestry. Every one of these changes is archived regardless, so **an archived change here does
not certify that its capability was verified on hardware** — and a ticked box certifies that a check
ran, not that it passed.

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

- **A verification run that only records is a commit; one that changes anything is a change.**
  Recording a run into the archived `design.md` that asked for it, ticking its box and updating this
  file is a `Record …` commit against the tracking issue — the pattern commit `ec8fe69` set when it
  closed issue #20. It becomes a change when the run does more than record: moves a constant a spec
  names, discovers a requirement the code does not meet, or produces design that does not belong in
  an archived change's `design.md` beside decisions made weeks earlier. Striking a question through
  with its answer is the archive's convention; adding new design there is not.
  `settle-win-x64-verification-debt` is the worked example of the second case and its Decision 1
  carries the argument.
- **By-hand tasks still open when a change is archived move to a tracking issue.** Issues #30 and
  #31 did this after the fact, for win-x64 and Apple Silicon respectively, and #49 does it for a
  suite that does not pass unattended. Doing it at archive time rather than afterwards is the rule
  because an unchecked box inside an archived `tasks.md` is visible to nobody who is not already
  reading that change — which is how eleven checks sat under an Apple Silicon heading that six of
  them did not belong to.
- **A harness written to drive a desktop-session check is kept under `spikes/`, not thrown away in a
  scratchpad.** `spikes -- fatal` is the instance: it drives the launch-observe-dismiss half of a
  fatal-startup reproduction, was written for `settle-win-x64-verification-debt`'s task 5.1, and is
  kept so change 12's CI can run it and so the next person reproducing one of these does not rewrite
  it. The state setup each reproduction needs stays by hand; that is not what the rule is about.

The reference repository is the behavioural specification — wire formats, the recording state machine
and its timing constants, the settings schema, and the error taxonomy all come from it. It is not a
.NET project, so nothing is copied; it is read and re-expressed. The built-in preset prompts noted
above are the one deliberate exception.
