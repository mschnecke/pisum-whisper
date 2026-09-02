## Context

This is the change that makes the product work, and it is also the change that adds the least new
mechanism. Capture, encoding, the Gemini round trip, the hook and the delivery all exist, are
specified, and are synced into `openspec/specs/`. What is missing is the component that decides
*when* each of them runs, what happens when one fails, and what the user is told. In shape it is a
state field, three timing rules and a `try`/`catch`.

The reference is `W:\github-pisum-transcript\src-tauri\src\hotkey\manager.rs`. It is 515 lines, but
roughly 300 of them are hotkey registration, parsing and the `global_hotkey` event loop — all of
which change 6 already owns and none of which is re-expressed here. What this change reproduces is
five functions: `handle_hotkey_press`, `stop_and_transcribe`, `process_and_transcribe`,
`transcribe_cloud` and `categorize_error`.

**The proposal predates changes 5, 6 and 7, and has drifted in five places.** Each is corrected in
the decisions below rather than silently ignored:

| The proposal says | What is true now |
|---|---|
| a 50 ms delay between setting the clipboard and pasting is a timing rule of this change | it shipped in change 7 as `TextOutput.DefaultSettleDelay`, step 4 inside the single method this change calls |
| map `AppException.Category` to user-facing messages | there is no `AppException`; there are four exception types, of which only `TranscriptionException` carries a category |
| a 200 ms toggle debounce is needed because auto-repeat rapidly toggles recording | `HotkeyMatcher.OnKeyPressed` already absorbs auto-repeat without raising an edge; the constant is kept, for a different reason |
| the reference's `catch_unwind` guard is dead code in its release profile | true, but the .NET hazard is a different one — an exception escaping the pipeline task is an *unobserved task exception*, which wedges the state machine with no crash and no log |
| notification call sites are "identified here" and implemented in change 11 | change 11 will **modify** this capability rather than fill in markers; what change 8 leaves it is a tested mapping function, not a seam |

Facts below marked *from the code* were read out of this repository during design.
`openspec/config.yaml`'s project context is stale in two ways that bear on this change — it names a
`Pipeline` folder that does not exist and a "hand-rolled Ogg muxer" that change 4 rejected in favour
of `Concentus.Oggfile` — so the folder naming below follows the six existing capability folders
instead.

## Goals / Non-Goals

**Goals:**
- Holding, or toggling, the configured hotkey produces the spoken words at the cursor of whatever
  application had focus. End to end, for the first time.
- Every timing rule and every concurrency guard in one component, testable without a microphone, a
  keyboard, a network or a clipboard.
- No dictation can wedge the application. Every path — success, failure, cancellation, an exception
  nobody predicted — returns the state machine to idle.
- No dictation runs unbounded, and quitting the application never destroys the user's clipboard.
- A recording signal change 9 can render, and a failure vocabulary change 11 can transport.

**Non-Goals:**
- Delivering notifications (change 11) or drawing a tray icon (change 9). This change writes to the
  log and nowhere else, which is a real limitation and is called out under Risks.
- Re-deciding anything owned elsewhere: the 50 ms settle delay and the clipboard restore guards
  (change 7), the encoder's format fallback (change 4), the retry policy, the key pool and the
  14 MiB inline ceiling (change 5), hotkey matching and suppression (change 6).
- Any change to the settings schema. The budget below is a constant, not a new field.
- Silence detection. The reference's RMS check lives in `transcribe_local`, and local Whisper
  inference is out of scope for this project; `transcribe_cloud` has no such check and neither does
  this.
- Transcript history, preview, editing, or a confirmation step.

## Decisions

**`DictationOrchestrator` is a concrete hosted singleton in `Core/Dictation/`.**
`Pisum.Whisper.Core.Dictation` gets `DictationOrchestrator`, `DictationState` and an internal
`DictationFailure`. `AddDictationPipeline` registers the orchestrator once and resolves it under two
roles — the singleton itself and `IHostedService` — following `AddGlobalHotkey`'s shape minus the
interface.

There is no `IDictationOrchestrator`. Every dependency it has is already an interface, so its tests
construct it directly over fakes, and its only other consumer is change 9's tray icon, in `App`,
which references `Core` anyway. `SettingsStore` is the precedent: a concrete singleton, consumed
directly, with no interface invented for it.
*Alternative rejected:* an interface for symmetry with `IGlobalHotkeyService`. That interface exists
because its implementation fills three roles and change 10 reuses it through `CaptureAsync`; neither
applies here.

It subscribes to `IGlobalHotkeyService.Pressed` and `Released` **in its constructor**, not in
`StartAsync`. *From the code:* `Host.StartAsync` resolves the whole `IEnumerable<IHostedService>`
before starting any of them, so constructing the orchestrator happens before
`GlobalHotkeyService.StartAsync` runs, and no edge can be missed in the window where the hook comes
up. `StartAsync` is therefore almost empty; `StopAsync` is where the work is.

**Nothing but a state transition runs on the hotkey's dispatch thread.**
*From the code:* `GlobalHotkeyService.DispatchAsync` reads its channel and invokes
`Pressed?.Invoke(...)` **synchronously** inside the read loop. A handler that awaits the pipeline
therefore blocks the loop — and in hold-to-record the `Released` edge that ends the recording is the
very next thing in that channel. The feature would deadlock against itself.

```
hook thread ──▶ Channel<HotkeyEdge> ──▶ dispatch thread
                                             │
                                             │  Pressed / Released  (synchronous invoke)
                                             ▼
                                    orchestrator handler
                                    ── claims a state transition, returns ──
                                             │
                                             └──▶ Task.Run: StopAsync, Encode,
                                                  TranscribeAsync, DeliverAsync
```

The handlers claim a transition and return; everything with a duration runs on a pooled task. This
is the same conclusion the reference reaches with `std::thread::spawn`, for the same reason, and it
is separately required by change 7: `TextOutput` sleeps for over a second and its own documentation
forbids calling it from a hook handler.

Starting the capture *is* left inline on the dispatch thread. `MiniAudioCapture.Start()` initialises
a `MiniAudioEngine`, enumerates devices and opens one, which is not instant — but moving it to a
task would mean a `Released` edge that can arrive before its `Pressed` has finished starting, and
sequencing that costs more than the latency does. The consequence is benign: the recording clock
starts after the device is open, so a key brushed during a slow start measures as a short recording
and is discarded, which is the correct outcome anyway.

**The published state is three values, because the orchestrator already holds three.**

```csharp
public enum DictationState { Idle, Recording, Transcribing }

public event EventHandler<DictationState>? StateChanged;
public DictationState State { get; }
```

The reference publishes one boolean and calls `tray::set_recording_state(false)` at the *end* of the
spawned pipeline thread, after the paste — so its icon claims "recording" throughout the upload. In
toggle mode a user who presses stop, sees the recording icon persist, and concludes the press did
not register will press again, and be told "Transcription In Progress" by an icon that just told
them they were still recording.

The flexibility objection runs backwards here. This component *must* distinguish `Recording` from
`Transcribing` internally, because the two guards differ — a press during `Recording` returns
silently, a press during `Transcribing` reports the in-progress message. Publishing a boolean means
adding a collapse step that discards information already held. Three values is the smaller change,
and change 9 can still render two icons from three states where it could not render three from two.
*Alternative rejected:* `bool IsRecording`, reproducing the reference. It costs an extra mapping and
buys a known UX defect.

Two details fixed here so change 9 does not have to guess: `Transcribing` is published when the
release is **claimed**, not when `StopAsync()` returns, so the icon answers the key immediately; and
`Idle` is published from the `finally`, so a *failed* dictation returns the signal exactly as a
successful one does. The event shape mirrors `SettingsStore.Changed` and is raised on a pooled
thread, which change 9's proposal already anticipates marshalling with `Dispatcher.UIThread.Post`.

**Ending a recording is an atomic claim, because *four* things can end it.**
A release edge (hold mode), a press edge (toggle mode), the max-duration watchdog and `StopAsync`
are all claimants, and the last two run on different threads from the dispatch loop. Shutdown is
easy to overlook — removing the event handlers does not retract a `Released?.Invoke` the dispatch
thread has already entered, and cancelling the watchdog cannot un-fire a delay that has already
returned — so it takes the same claim as the other three rather than reading the state and acting on
the answer afterwards. A plain
`if (state == Recording) { … }` lets a watchdog firing at the same moment as a real release run the
pipeline twice over one capture. The reference gets this right with `ACTIVE_RECORDER.take()` under a
mutex; here the whole transition is taken under a single `Lock`, and the loser of the race finds the
state already moved and returns.

The state field is touched from two thread contexts — the dispatch thread for edges, a pooled thread
for pipeline completion — so one lock is required regardless. It is uncontended in practice: edges
arrive milliseconds apart from a human, and the pipeline completes once.

**The max-duration watchdog is a `CancellationTokenSource` and a `Task.Delay`, cancelled when the
recording ends.** `AppSettings.MaxRecordingDurationSecs` (default 600) is read when the recording
starts. The reference spawns a thread that sleeps the entire duration on *every* recording and leaks
one per dictation; a `Task.Delay` on a token that the normal stop cancels costs nothing and leaves
nothing behind. When it does fire it takes the same atomic claim as a release, and the recording is
transcribed rather than discarded — the audio is real and the user was speaking.

**Two cancellation tokens, with different scopes.**

```
 ┌─ shutdown (orchestrator lifetime, cancelled by StopAsync) ─────────────┐
 │                                                                       │
 │   StopAsync()      Encode()      ┌── linked: shutdown + 120 s ──┐      │
 │   ───────────      ─────────     │      TranscribeAsync         │  DeliverAsync
 │   uncancellable    CPU-bound     └──────────────────────────────┘      ▲
 │                                                                       │
 └──── shutdown alone, never the budget ─────────────────────────────────┘
```

*Why a budget exists at all.* *From the code:* `GeminiProvider.MaxAttempts` is 3 with 1 s and 2 s
backoff, `GeminiHttpClient.Timeout` is 60 s **per request**, and `GeminiProviderPool` walks every
enabled entry. A hung connection — a black-holed route, captive-portal WiFi, a VPN dropping
mid-upload — therefore costs 3 × 60 s + 3 s = **183 s per configured key**, so 366 s with two and
549 s with three. Throughout that time the state machine sits in `Transcribing`, every hotkey press
answers "Transcription In Progress", and in this change there is no tray menu and no notification to
explain it. The application is silently unusable for six minutes.

Fast failures never approach this. A 401 is thrown immediately by `FailureFor` without a retry, and
a 429 answers in milliseconds, so three attempts plus backoff is about 3 s. The budget only ever
bites on a hang.

*The number is 120 s*, which allows one full 60 s request and one retry. A legitimate transcription
is already bounded by the per-request timeout; 10 minutes of Opus at 24 kbps is about 1.8 MB, which
flash-lite answers in tens of seconds. The honest consequence: with three keys and a hung network,
the third key is never tried. That is the right trade, because fallback exists for *fast* failures,
and those cost seconds each.

It is a constant, not a setting. The reference has no such field, and the settings schema is the
reference's to define.

*The plumbing already exists and is correct.* The pool calls `ThrowIfCancellationRequested` once per
fallback attempt, and `GeminiProvider`'s catch filter —
`when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)`
— is what makes this work: the client's *own* 60 s timeout also throws `TaskCanceledException` and is
caught and retried, while ours fails the filter and propagates. So an expired budget surfaces as
`OperationCanceledException`, not `TranscriptionException`, and this change is only creating the
token.

*Delivery is deliberately outside the budget.* `TextOutput` spends more than a second waiting before
its restore by design. A budget clock that expired during transcription must not then cut a delivery
short; delivery gets the shutdown token only, which is exactly what change 7 wrote its cancellation
contract for.

**`StopAsync` cancels *and awaits*, and the awaiting is a correctness requirement.**
Change 7's design already registered this as an obligation on this one:

> *"It follows that change 8 must await a delivery in progress during `StopAsync` rather than
> dropping it; a note for that design, and the reason `DeliverAsync` takes a token at all."*

The hazard is precise. Between `TextOutput`'s clipboard write and its restore, the user's previous
clipboard text exists nowhere but in that call — and on Windows `SetClipboardData` hands ownership to
the system, so the transcript outlives this process. Cancelling without awaiting lets the process
exit inside that window and destroys the user's clipboard permanently.

The timing works out against `Program.cs`'s `host.StopAsync(TimeSpan.FromSeconds(5))`: a cancelled
transcription aborts in milliseconds, and a cancelled delivery finishes in about the settle delay
plus the paste, because `WaitBeforeRestoreAsync` catches the cancellation and shortens the wait
rather than skipping the restore.

| state at Quit | what happens | why |
|---|---|---|
| `Idle` | nothing | — |
| `Recording` | stop the capture, discard the samples | nobody is waiting on a dictation they did not finish |
| `Transcribing` | cancel, then **await** the pipeline task | the clipboard hazard above |

**A state subscriber cannot break a dictation.** The announcement is the pipeline task's first act,
so an exception from `StateChanged` would skip the whole dictation — the capture never closed, the
capturing flag never cleared, the state at `Transcribing` for ever. `Announce` therefore catches and
logs. This is the same wedge `RunAsync`'s catch exists to prevent, and it has to be prevented in both
places because the announcement runs outside that catch.

**Budget expiry and shutdown are the same exception and must be told apart.**
Both arrive as `OperationCanceledException`. Budget expiry is a failure the user needs to hear about
("the transcription timed out"); shutdown is something they asked for and must be silent. The branch
is a check of the shutdown token inside the catch, and it exists deliberately rather than by
accident.

**The capture sample rate becomes a constant on `IAudioCapture`.**
This change is the first caller that has to hand `IAudioEncoder.Encode(samples, sampleRate, format)`
a number, and *from the code* there is nowhere to get one: `StopAsync()` returns a bare `float[]`,
and `MiniAudioCapture.SampleRate` is `private const`. There are already twelve `48_000` literals in
the test projects, one of them in `ManualCaptureSmokeTest` — which is this orchestrator in
miniature, capturing and then encoding, and which had nowhere to get the number either.

```csharp
public interface IAudioCapture
{
    /// <summary>The rate every capture is requested at; fixed by the audio-capture spec.</summary>
    public const int SampleRate = 48_000;
    …
}
```

The constant belongs on the interface rather than the implementation because it is a property of the
*contract* — the `audio-capture` spec requires 48 kHz mono to be requested and the backend to
convert — not of miniaudio. An orchestrator reaching past `IAudioCapture` to a concrete type for a
number the interface already promises would be the wrong dependency.

The reason to care is that a wrong value fails quietly and expensively. `OggOpusWriter` passes the
rate straight to `OpusCodecFactory.CreateEncoder`, Opus permits only 8/12/16/24/48 kHz, and
`AudioEncoder` catches **broadly by design** and falls back to WAV with a single `LogWarning`. A
mistyped constant would therefore silently disable Opus for every user, and *from the code* the
14 MiB inline ceiling is reached after about 2 min 33 s of WAV against 81 minutes of Opus — so
recordings over about two and a half minutes would start failing with "the recording is too large to
send… switch the audio format to Opus", advising a setting the user already has.
*Alternatives rejected:* a thirteenth literal in the orchestrator, which leaves that hazard
unguarded; `int SampleRate { get; }` on `IAudioCapture`, which models a variation the spec forbids;
and `StopAsync` returning a `Recording(float[] Samples, int SampleRate)` record — the type-correct
answer, symmetric with `EncodedAudio` on the output side, but it modifies an archived, synced
capability and refactors change 4's code and tests for one argument at one call site whose value
cannot vary. The existing twelve literals are deliberately **not** migrated; they are not broken,
and they now have a home to move to.

**The wall clock and the sample count are both checked, and they are not redundant.**
The reference performs two separate tests — `elapsed < MIN_RECORDING_DURATION` discards silently, and
`samples.is_empty()` raises "No audio recorded" — and reads at first like duplication. It is not.
Together they are a diagnosis:

| wall clock | samples | what happened | what the user gets |
|---|---|---|---|
| < 50 ms | any | the key was brushed | nothing at all, silently |
| ≥ 50 ms | none | the microphone is dead, muted or misrouted | a Recording Error saying so |
| ≥ 50 ms | some | a real dictation | a transcript |

Merging them into one sample-count measurement would look tidier and would silently discard every
dictation from a broken microphone: the user holds the key, speaks for ten seconds, releases, and
gets nothing, forever, with no explanation. The wall clock is what rules out the brush and so earns
the right to call an empty capture a fault.
*Alternative rejected:* deriving the duration from `samples.Length / IAudioCapture.SampleRate`, which
is a more accurate measure of what was recorded and destroys the distinction that matters.

**The 200 ms toggle debounce is kept, for a reason the reference does not give.**
Its stated purpose is keyboard auto-repeat, and *from the code* that condition can no longer occur:
`HotkeyMatcher.OnKeyPressed` returns `new MatchResult(null, true)` for a repeat — suppressed, but no
edge — so a held binding produces exactly one `Pressed`. What survives is a rapid human double-tap in
toggle mode, and there the debounce covers a band the minimum-duration rule does not:

```
double-tap gap   0 ─────────── 50 ms ─────────── 200 ms ───────────▶
                 │  min-duration  │   debounce     │  a real dictation
                 │  discards it   │   ignores it   │
```

Without it, a fumbled press between 50 and 200 ms encodes a fraction of a second of audio, uploads
it, and earns the user a "Transcription Error" notification for a slip of the finger. The constant is
retained and re-justified here explicitly, because a future reader who checks only the reference's
comment will find its reason obsolete and delete it.

**The failure vocabulary is a mapping function, not a seam.**
The reference sends eight notifications from `manager.rs`, and *from the code* they are not one kind:
`send_notification` forces, `send_info_notification` reads `show_tray_notifications` and returns
early. Splitting on that line puts the ownership boundary in an obvious place:

```
        change 8                              change 11
 ┌───────────────────────────┐      ┌─────────────────────────────┐
 │ which failure is this?    │      │ forced or suppressible?     │
 │ what does it say?         │─────▶│ how does it reach the OS?   │
 │   → (title, message)      │      │   → toast / osascript       │
 └───────────────────────────┘      └─────────────────────────────┘
```

The `force` flag is a read of `ShowTrayNotifications` — notification policy, not pipeline business.
Title selection is this change's, and it has real branching: by type for three exception kinds, then
by `ErrorCategory` for the fourth.

```csharp
internal static (string Title, string Message) Describe(Exception exception);
```

| thrown by | type | title |
|---|---|---|
| capture, encoding | `AudioException` | Recording Error |
| transcription | `TranscriptionException` | by `ErrorCategory`: Configuration / Network / Authentication / Rate Limit / Transcription Error |
| clipboard write | `TextOutputException` | Output Error |
| budget expiry | `OperationCanceledException`, shutdown token not cancelled | Transcription Error |
| anything else | — | Unexpected Error |

It stays `internal` — `Pisum.Whisper.Core.csproj` already carries `InternalsVisibleTo` for
`Core.Tests` — because change 11's notifier will be injected into this orchestrator rather than
calling the mapping from outside.
*Alternatives rejected:* an event (`EventHandler<Notice>`), whose strings would be computed and
dropped for three changes with no consumer to offset it, unlike `StateChanged` whose consumer is one
change away and whose value is already held; an `IUserNotifier` with a logging implementation, which
is an abstraction over `ILogger` doing what `ILogger` does, the same objection `AddTextOutput`
already records against wrapping `IEventSimulator`; and comments alone, which rot and leave change 11
re-deriving the mapping.

Three of the eight sites are not exceptions and do not go through `Describe`: the in-progress guard
and the auto-stop notice are status with no branching, and get plain log statements. The third is
`TextOutputOutcome.ClipboardOnly`, which is a returned value — and *from the code* `TextOutput`
**already logs the diagnosis**, on both degraded paths ("the paste keystroke could not be sent",
"the focused application cannot be reached"). This change does not repeat that. What it does record,
once, is the user-facing message, because the `dictation-pipeline` spec requires the user to be told
the text can be pasted manually and in this change the log is the only place to tell them — the same
treatment the other two status sites get.

The consequence, stated rather than discovered later: **change 11 modifies `dictation-pipeline`.** It
needs the outcome and the failure surfaced to notify on them, and no seam left here would change
that.

**The macOS "Microphone Access Required" message is deferred, with its reason.**
The reference distinguishes it with `#[cfg(target_os = "macos")]` and a
`e.to_string().contains("No input device")` test — a substring match on an error message, which is
the mechanism this codebase has twice rejected: `ErrorCategory`'s own documentation cites the
reference's substring matching as the thing it exists to avoid, and CLAUDE.md records categories as
*"fixed where the failure is raised rather than re-derived from message text by the caller."*
`AudioException` carries no category, so reproducing this means doing exactly that.

And *from the platform verification matrix*, nobody has seen the failure: spike S2 records **PASS** on
the M4, meaning the microphone was accessible, so the denied-grant path was never exercised. It is
unverified whether a refused macOS microphone grant even presents as zero capture devices rather than
as a device delivering silence. So this change describes every `AudioException` as "Recording Error"
with its own message, and the macOS-specific guidance waits for evidence.
*Alternative rejected:* adding a category or subtype to `AudioException`, which is the right fix if
the distinction turns out to be needed — but it modifies change 4's archived spec on the strength of
a guess about behaviour nobody has observed.

**A failure never leaves the pipeline unable to start another dictation.**
The whole background task is wrapped, and the `finally` resets the state to `Idle` and publishes it.
The .NET hazard here is not the reference's: an exception escaping a `Task.Run` becomes an
*unobserved task exception*, which by default does not crash the process. It vanishes, the state
stays `Transcribing` for ever, and the hotkey answers "Transcription In Progress" until the user
restarts the application. The `try`/`catch`/`finally` is there for the state reset first and the
message second.

**Settings are read per dictation from `SettingsStore.Current`, and never cached.**
The recording mode, the maximum duration, the audio format and the active preset's system prompt are
all read when they are needed. This follows `GeminiProviderPool`'s precedent exactly — it reads the
enabled entries per call rather than being rebuilt on a change subscription, because
`SettingsStore.Current` is the authoritative in-memory store the reference lacks. There is no change
subscription and no rebuild step here either. The active preset resolves by construction:
`SettingsStore.Load` repairs an `ActivePresetId` that matches no preset back to
`BuiltinPresets.DefaultId`, which is the guarantee `ITranscriptionProvider` already documents as its
reason for taking the prompt as a parameter.

**Logging.** The transcript is never written, at any level, per change 3's rule and change 7's
extension of it — character counts, categories and outcomes only. Hotkey edges are **not** logged
here: change 6's dispatch loop already writes `Hotkey {Binding} {Edge}` at `Information` for every
edge, and a second line per edge would double the most common lines in the file. What this component
logs is the state transitions, the discard reasons, the elapsed timings and the described failure.

**Verification.** `DictationOrchestrator` is unit-tested in `Core.Tests/Dictation/` over fakes for
all five dependencies, with the timing constants and the budget injected through an internal
constructor — the shape `GlobalHotkeyService` and `TextOutput` both already use — so no test waits
50 ms, 200 ms or 120 s. Every rule in the spec is reachable that way: both modes, both discard paths,
all three claimants of a recording, the guards, budget expiry, shutdown mid-transcription and
mid-delivery, and that a thrown pipeline returns to `Idle`.

What unit tests cannot cover is that the thing works, and this change is the first with an end-to-end
story: press the hotkey, speak, release, and the words appear. That is a manual smoke test in the
manner of `ManualCaptureSmokeTest`, `ManualTranscriptionSmokeTest` and `ManualClipboardRoundTrip` —
run by name, not in the suite.

## Risks / Trade-offs

**The budget can cut off a transcription that would have succeeded.** 120 s allows one full request
and one retry; a genuinely slow upload on a poor connection could exceed it. → The alternative is the
six-minute wedge described above, which is worse and invisible. The number is a constant in one place
and is listed as an open question rather than a settled measurement.

**With three keys and a hung network, the third key is never tried.** → Accepted deliberately.
Fallback exists for fast failures, which cost seconds; a hang that consumes the whole budget is not a
case where trying another key was going to help.

**Change 8 has no user-visible output at all.** No tray icon until change 9, no notifications until
change 11. A failed dictation, an auto-stopped recording and a refused second press are all
log-only. → Inherent to the roadmap ordering and already a non-goal, but it means the manual smoke
test is the only feedback loop this change has, and a user running this build sees nothing when
something goes wrong.

**Capture start runs on the dispatch thread, so a slow device delays the recording.** → The recording
clock starts after the device is open, so the effect is a slightly later start rather than a
mis-measured duration; and the alternative sequencing costs more than the latency. Unmeasured.

**The watchdog fires on a pooled thread and races a real release.** → The atomic claim makes the
loser a no-op. The residual is cosmetic: which of the two log lines appears first.

**A dictation started in toggle mode can still be recording when the user quits.** → `StopAsync`
stops the capture and discards. The samples are lost, which is correct — the user asked to quit
mid-recording — but nothing tells them, because notifications are change 11.

**`TextOutput` adds about a second to the tail of a dictation.** Change 7's design flagged this
explicitly as something this change should consider. → `Idle` is published when the pipeline task
completes, so the tray returns to idle about a second after the text has appeared. Accepted for now:
publishing it earlier would mean the state no longer bounds the guards, and a failed restore would
have nowhere to be logged against. Revisit in change 9 if the delay proves visible.

## Open Questions

**Is 120 s the right budget?** Chosen from the shape of the retry policy rather than from
measurement. Nobody has timed a real ten-minute dictation through flash-lite on a slow uplink. A task
of this change is to run one and record the result here.

**Does a denied macOS microphone grant present as "no input device"?** Unverified — S2 passed on the
M4 with the microphone accessible. It decides whether the deferred "Microphone Access Required"
message ever needs a home, and if so whether `AudioException` needs a category. **Attempted
2026-09-02 (issue #31, task 8/6.4) and abandoned before reaching a denied grant to test against.**
Microphone access, unlike Accessibility, is tracked in the *user*-level TCC database and attributed
directly to the app binary's own file path rather than to a responsible parent process —
`Pisum.Whisper.App` already held its own grant there, separate from Rider's. `tccutil reset
Microphone <path>` refused it ("No such bundle identifier"): `tccutil` expects a bundle identifier,
which an unbundled dev build does not have. Writing the revocation directly into the live TCC
database was ruled out as too risky — it is an actively-managed system security database, not a
config file, and a manual write while `tccd` holds it open risks corrupting it or leaving it
inconsistent with the daemon's own state. Revoking it by hand through System Settings → Privacy &
Security → Microphone was offered and declined for this sitting. Still unverified.

**Should a timed-out or failed dictation keep its audio?** Today the encoded bytes are dropped, as in
the reference, and the user re-speaks. Retaining them would make a retry possible and is cheap now
and awkward later — but it is new scope, with a place to put the file and a lifetime to decide. Not
taken here; recorded so the decision is visible rather than implicit.

## Verification results

Run on 2026-09-02 on win-x64 (Windows 11 Pro 10.0.26200) under `dotnet run --project src/Pisum.Whisper.App`
(Debug), as task 6.1 of settle-win-x64-verification-debt. The configured provider's model needed
changing during this run — `gemini-flash-lite-latest` returned Gemini 400 `INVALID_ARGUMENT` on the
first attempt, and `gemini-2.5-flash`, `gemini-2.5-flash-lite` and `gemini-pro-latest` were also tried
(404, 404 and 429 respectively) before `gemini-3-flash-preview` succeeded; that is a provider
configuration detail external to this codebase, not a pipeline defect.

| # | What was checked | Result |
|---|---|---|
| 6.1 | Hold-to-record: hotkey held, spoken, released, into Notepad with known text pre-loaded on the clipboard | **PASS** — a 3.2 s recording (153600 samples, Opus); the log's delivery line reads `Delivered 44 characters: Pasted.`; the known clipboard text was back afterwards |
| 6.1 | Toggle mode: hotkey pressed to start, spoken, pressed again to stop, same setup | **PASS** — a 4.4 s recording (212640 samples, Opus); `Delivered 45 characters: Pasted.`; clipboard restored |

No transcript text is recorded above, per this change's own logging rules — only durations, sample
counts and character counts, all read from the log.
