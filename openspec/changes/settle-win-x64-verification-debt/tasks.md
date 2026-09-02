## 1. Preparation

- [ ] 1.1 Establish the baseline: `git status` clean on `main` and the commit noted;
  `dotnet build Pisum.Whisper.slnx` at 0 warnings; `dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual`
  with the total it prints noted, which task 6.1 compares against; `dotnet build src/Pisum.Whisper.App -c Release`
  with `src/Pisum.Whisper.App/bin/Release/net10.0/Pisum.Whisper.App.exe` confirmed present. Verify: the
  three commands succeed and the commit, the test total and the exe path are written to the scratchpad.
- [ ] 1.2 Back up user state: copy `~/.pisum-whisper.json` to the scratchpad and record its SHA-256;
  record the `Run` value `Pisum Whisper` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  (absent, or its path) and `HKCU\Software\Microsoft\Clipboard\EnableClipboardHistory` (absent, or its
  value). Verify: `Get-FileHash` of the backup equals the recorded hash, and both registry readings are
  written down beside it.

## 2. The first dictation and what rides on it

- [ ] 2.1 **8 / 6.1** Under `dotnet run --project src/Pisum.Whisper.App`, put known text on the
  clipboard, focus Notepad, hold the hotkey, speak a sentence, release; confirm the words arrive at the
  cursor and the known text is back on the clipboard afterwards. Set `recordingMode` to toggle and
  repeat with press, speak, press. Then create change 8's *Verification results* section in its
  archived `design.md`, in change 7's format, with a row per mode carrying the durations and character
  counts from the log and no transcript text, and tick 6.1 in its `tasks.md`. Verify: both rows record
  `Pasted` from the log's delivery line and the clipboard restore; `git diff` of the archived change
  shows the new section and the tick and nothing else.
- [ ] 2.2 **9 / 4.1** Launch the Release build with `Start-Process`: no taskbar button, no console
  window; find the icon and record where it landed (expect the hidden overflow, which is change 11's
  evidence); hover for the tooltip naming the active preset; run a dictation and watch
  idle → recording → transcribing → idle; open Settings → Presets, activate the other preset and hover
  again — the live refresh the archived task said change 10 would have to prove; Quit. Record in
  change 9's `design.md` under a new *Verification results*, noting that the tooltip check now covers
  the live refresh, strike this change's open question 9, tick 4.1. Verify: all three icons were
  observed, the tooltip named both presets without a relaunch, and the record names the overflow.
- [ ] 2.3 **10 / 6.3** With `logLevel` at `debug`, paste an API key into a provider field and count the
  `Committing … settings changes.` lines (expect one); type one character and press Tab at once, then
  read the file a second later (the edit is there); type normally across several fields and judge
  whether any commit is perceptible. Move `SettingsEditor.CommitDelay` once if any of the three fails,
  with its doc comment and `CLAUDE.md`'s "400 ms"; otherwise leave it. Record in change 10's
  `design.md` — a new *Verification results* section, open question 3 struck through — and tick 6.3.
  Verify: one commit per paste in the log; the tab-away edit is in `~/.pisum-whisper.json`; if the
  constant moved, `dotnet test tests/Pisum.Whisper.App.Tests` is green and the reason is in the record.
- [ ] 2.4 **10 / 6.4** With the Hotkey tab recording, press the configured binding: no dictation starts
  and the log shows no press edge; cancel the capture and dictate — the binding works. Click Change
  again, click into another application without cancelling, and dictate at once — the binding works
  immediately. Record beside 2.3 and tick 6.4. Verify: the log shows no `Pressed` during the capture
  and a normal dictation right after each of the two endings.
- [ ] 2.5 **7 / 3.3** Turn clipboard history on (*Settings → System → Clipboard*), leaving *Sync across
  devices* off; dictate into Notepad; open Win+V: the transcript is absent, an ordinary Ctrl+C from
  Notepad is present, and the restored previous text added no second entry. Clear the history and
  turn it back off. Record in change 7's *Verification results*, replacing the "could not be
  performed" bullet with a row and noting that the cloud half stays unverified; tick 3.3. Verify:
  `EnableClipboardHistory` reads as it did in 1.2, and the record names all three observations.

## 3. The budget

- [ ] 3.1 **8 / 6.3** Under `dotnet run` with `audioFormat` Opus, play speech at the microphone and
  record four times: 60 s and 300 s in toggle mode, and 600 s twice, stopped by the watchdog. Read
  each round trip from the log as the interval between the `Transcribing a … s recording` line and
  the delivery or failure line, and note the connection's measured uplink. Apply the two rules in
  Decision 4: `GeminiHttpClient.Timeout` at 60 s stands if the slowest 600 s attempt is under 30 s,
  else becomes twice the slowest, rounded up; `DictationOrchestrator.DefaultTranscriptionBudget`
  stays at twice the timeout plus the waits, so 120 s stands unless the timeout moved. Whatever
  moves, moves once, with its doc comment, `CLAUDE.md`'s sentences and — for the budget — the number
  in this change's `specs/dictation-pipeline/spec.md`. Record the date, model id, connection, uplink,
  sample counts, four timings and the slope in change 8's *Verification results*, with the
  32 kbps-per-second uplink arithmetic beside the WAV ceiling as a known limit; strike "Is 120 s the
  right budget?"; tick 6.3. Verify: four timings and a slope in the record and no transcript text;
  the constants, the delta spec and `CLAUDE.md` agree; `dotnet build` at 0 warnings.

## 4. Notifications and autostart

- [ ] 4.1 **11 / 7.1, and the alt-tab half of 7.4** Under `dotnet run`: delete `~/.pisum-whisper.json`
  (backed up in 1.2), launch, confirm the welcome notification and the self-opened settings window.
  With *Show tray notifications* off, press the hotkey during a transcription (nothing is shown) and
  hold it with the input device disabled in Sound settings (the recording failure is shown). While a
  notification is up, keep typing in Notepad and dictate immediately after — focus never moved and
  the paste lands there. Hold Alt+Tab with a toast up and note whether it is listed (predicted
  absent; this change's open question 4 says why). Then click a toast once, note where keyboard focus
  goes, and hold Alt+Tab again: a toast that was activated by the click is predicted to be listed
  from then on. A focus move is recorded as a finding, not fixed here. Switch *Start with system* on
  and off, reading the `Run` value each time. Restore the settings file and re-hash it; re-enable the
  input device. Record in change 11's `design.md` under a new *Verification results*, strike the
  alt-tab open question, tick 7.1; 7.4 stays open until 4.2. Verify: the hash matches 1.2; the `Run`
  value appeared naming the Debug output and disappeared; the alt-tab answer before and after the
  click, and where focus went, are in the record.
- [ ] 4.2 **11 / 7.4, the experiment** Insert the blocking
  `Task.Run(… Notify(…)).GetAwaiter().GetResult()` from Decision 5 between `host.Start()` and
  `BuildAvaloniaApp` in `Program.Main` and run under `dotnet run`. Expected, from the reading: a
  `Fatal` line carrying `InvalidOperationException` "The calling thread cannot access this object
  because a different thread owns it", the "could not start" dialog, exit code 1, no tray icon.
  Record the four observations regardless — the exception, the dialog, whether the tray icon came up
  and swapped on a dictation, whether a later notification rendered. Revert with
  `git checkout -- src/Pisum.Whisper.App/Program.cs`. Strike the dispatcher question in change 11's
  `design.md` with the observations and the mechanism, and note that the archived task's headless
  half was not expressible against a `MarkReady()` gate and is against `FromThread` (4.3). Verify:
  `git status` clean; the record names the thread the notification was raised from, quotes the
  exception, and says whether the outcome matched the prediction.
- [ ] 4.3 **11 / 7.4, the gate** Add the readiness gate from Decision 5 to `ToastPresenter`:
  `ILogger<ToastPresenter>` through the public constructor, the UI thread captured at construction
  and injectable through the internal one, and a `Present` that asks `Dispatcher.FromThread` for that
  thread's dispatcher — posting to it when there is one, and otherwise logging the warning with the
  title only and returning. Add `PresentBeforeTheUiThreadHasADispatcherIsDroppedAndLogged` (the
  presenter constructed naming a fresh thread that owns no dispatcher; asserts the drop and the
  warning) and `PresentAfterTheUiThreadHasADispatcherShows` to `ToastPresenterTests`; the existing
  four tests pass `NullLogger<ToastPresenter>.Instance`. Re-run 4.2's experiment: the early
  notification is dropped with a warning, the application comes up, a later notification renders.
  Record in change 11's `design.md` and tick 7.4. Verify: `dotnet build` at 0 warnings;
  `dotnet test tests/Pisum.Whisper.App.Tests` green including the two new tests; the repeated
  experiment passes with the warning in the log; the `notifications` delta's second scenario is what
  the drop test asserts. Skip only if 4.2 contradicted the reading, and then record why.

## 5. Startup failures

- [ ] 5.1 **report-startup-failures / 7.1, unwritable with none present — and the driver** First add
  `spikes -- fatal <exe> <title>` to `spikes/Pisum.Whisper.Spikes`: launch the executable, wait for a
  top-level window of that process whose title matches, post `WM_CLOSE` (which an `MB_OK` box answers
  as OK), wait for exit, and print the exit code and the newest `Fatal` line from
  `~/.pisum-whisper/logs/pisum-whisper.log`. It is the 2026-09-02 run's script, kept this time so
  change 12's CI can run it; the state setup for each reproduction stays by hand. Then: move the
  settings file aside (the backup exists), create a directory named `~/.pisum-whisper.json`, and run
  the driver against the Release build: a Settings Error dialog naming the path, exit code 1, a
  `Fatal` line. Record which exception it was. Remove the directory and any `.pisum-whisper.json.tmp`,
  restore the file, re-hash. Add the row to `report-startup-failures`' *Verification results*.
  Verify: `dotnet run --project spikes/Pisum.Whisper.Spikes -- fatal` builds and drives the
  reproduction end to end; the hash matches 1.2, no `.tmp` remains, and the row names the exception
  type and the exit code.
- [ ] 5.2 **7.1, `ValidateOnBuild`** Comment out `AddNativeOutput()` in `Program.BuildHost`,
  `dotnet build src/Pisum.Whisper.App -c Release`, run the driver: a dialog with the "could not
  start" title, exit code 1, a `Fatal` line naming `ISystemClipboard`.
  `git checkout -- src/Pisum.Whisper.App/Program.cs` and rebuild Release. Add the row. Verify:
  `git status` clean apart from the new spike, and the Release exe is rebuilt from the clean tree
  before 5.3.
- [ ] 5.3 **7.1, missing tray asset** Move `src/Pisum.Whisper.App/Assets/tray-idle.png` to the
  scratchpad, rebuild Release, run the driver: the "could not start" dialog, exit code 1, a `Fatal`
  line carrying the `avares://` URI. Move it back, `git status` clean apart from the spike, rebuild
  Release. Add the row, annotate the archived task with the correction from Decision 6, and tick 7.1
  now that all four reproductions have rows. Verify: `git status` clean apart from the spike; four
  rows under 7.1; the archived task text says the build output holds no PNG.
- [ ] 5.4 **7.3 at login** Last of all. Launch Release, make sure *Start with system* is on, confirm the
  `Run` value names the Release exe, Quit; corrupt the settings file as in Decision 7; sign out and in;
  note whether the dialog is in front of or behind what login raised, and what that was; dismiss;
  restore the file and re-hash; confirm the `Fatal` line's timestamp is the login; launch once more so
  the reconciler aligns the `Run` value with the restored file, Quit. Strike the `MessageBoxW`
  question and tick 7.3. Verify: the hash matches 1.2; the `Run` value reads as in 1.2, the switch
  toggled if the recorded path differs; the record names the foreground window at login.

## 6. Rider

- [ ] 6.1 **migrate-tests / 5.4** Discovery is already evidenced by Rider's own session logs
  (Decision 8), so this is the person-only remainder: open the Unit Tests window, compare its total
  with 1.1's `dotnet test` total, and run one test from the gutter. Record in the migration's
  *Verification results* — the two logged sessions with their dates and element counts, then the
  window total and the gutter run — and tick 5.4. Only if the window disagrees with its own logs:
  check the Testing Platform support setting first, and treat the VSTest-bridge fallback as a
  separate decision rather than taking it here. Verify: the two totals are equal and the gutter run
  is green, and the record names both logged sessions.

## 7. Bookkeeping

- [ ] 7.1 Documents: this change's `design.md` gains a *Verification results* table with one row per
  check pointing at the archived section; `ROADMAP.md`'s *Artifact status* table drops the win-x64
  entries from its seven rows and its "Ten of the twenty-three" paragraph is rewritten to what
  remains; the *Off-sequence changes* table gains a row for this change and *Standing decisions*
  gains three rules — Decision 1's on when a verification run is a commit and when it is a change,
  that by-hand tasks still open at archive move to a tracking issue as #30 and #31 did after the
  fact, and that a harness written for a desktop-session check is kept under `spikes/`; `CLAUDE.md`'s
  spikes list gains `fatal`; `README.md`'s "No dictation
  has yet been run end to end by hand" paragraph and `CLAUDE.md`'s "archived with their manual
  verification still open" sentences, plus any constant or test-stack claim that moved, are
  rewritten. Verify: `grep -rn "No dictation has yet\|verification still open" README.md CLAUDE.md openspec/ROADMAP.md`
  returns only lines that are still true, and every number `CLAUDE.md` gives for the timeout, the
  budget and the commit delay matches the code.
- [ ] 7.2 Close out: post the results table on issue #30 as a comment — outcomes and any constant that
  moved, no transcript text — and close it, after confirming with the user; commit the `openspec/`
  edits with a subject that leads with "Record …", per `CLAUDE.md`, and any code change separately.
  Verify: `gh issue view 30 --json state` reports `CLOSED`; the openspec commit's subject names the
  act of recording; the two delta specs are ready for `/opsx:archive` to sync.
