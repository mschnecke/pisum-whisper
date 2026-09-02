## Context

This is the last thing that happens in a dictation and the only part of it the user actually sees.
Change 8 has nothing to deliver a transcript with until it exists, and every earlier change is
invisible without it: the hotkey, the capture, the encode and the Gemini round trip all end at a
string in memory that has to arrive at the cursor of whatever application the user was typing in.
No cross-application text insertion API exists on either target platform, so the mechanism is the
clipboard plus a synthetic paste keystroke — the same mechanism the reference uses, for the same
reason.

The reference's `output/` module is two files and 50 lines: set the clipboard with `arboard`, sleep
50 ms, send Ctrl+V (Cmd+V on macOS) with `enigo`, and on failure notify *"Text was copied to
clipboard but paste simulation failed. Use Ctrl+V to paste manually."* It never puts the user's
previous clipboard back. Restoring it is a standing decision recorded in `openspec/ROADMAP.md`, and
it is where nearly all of this design's complexity lives: everything the reference does is three
steps, and the restore adds three more, each with a way to go wrong that destroys either the user's
clipboard or the transcript they just spoke.

Change 1's spikes bear directly on this change and one of their findings is load-bearing: SharpHook
flags injected events (`EventMask.SimulatedEvent`, `KeyboardHookEventArgs.IsEventSimulated`), and
change 6's `HotkeyMatcher` already returns `MatchResult.Ignore` for a simulated event on **both**
edges. The paste keystroke this change sends while the hook is live is therefore already invisible
to the hotkey, with no suppression scheme needed. What the spikes also left behind is a **FAIL**:
S1b's simulated paste landed in a foreign application on win-x64 and did not on osx-arm64.

Facts below marked *from the assembly* were read out of Avalonia 12.1.1's and SharpHook 8.0.0's
shipped metadata during design, using the `api` spike. Facts marked *from the spikes* come from
`openspec/changes/archive/2026-08-27-bootstrap-solution/design.md`.

## Goals / Non-Goals

**Goals:**
- The transcript arrives at the cursor of the focused application, whichever application that is.
- The user's previous clipboard contents survive a dictation.
- A failed paste degrades to a usable state — the transcript on the clipboard and a message saying
  so — rather than losing the result.
- The operating system does not keep a copy of the transcript once the dictation is over.
- A transcript is never lost without the user being told, where the platform gives us any way to
  know.
- One seam change 8 can call and one outcome it can present, with the timing and the restore rules
  on this side of it.

**Non-Goals:**
- Preserving non-text clipboard contents. An image or a file list is read as "no text to restore"
  and is not round-tripped; the transcript replaces it.
- Per-application special-casing, UI Automation text insertion, or typing the transcript keystroke
  by keystroke instead of pasting.
- Transcript history, re-pasting an earlier result, rich text, formatting.
- A setting to turn the restore off. The restore is the behaviour, not an option.
- Deciding *when* a dictation produces a transcript, or presenting the outcome to the user. Change 8
  orchestrates and change 11 notifies.

## Decisions

**One service owns the whole sequence: `ITextOutput.DeliverAsync`.**
`Pisum.Whisper.Core.Output.ITextOutput` exposes a single `Task<TextOutputOutcome> DeliverAsync(string
transcript, CancellationToken ct)`, and `TextOutput` performs all six steps behind it:

```
 0. trim the transcript                        nothing left after trimming -> ArgumentException
 1. previous = clipboard.TryGetText()          best effort; a failure logs and continues
 2. clipboard.SetText(transcript)              hard failure -> TextOutputException
 3. probe.CanPaste()                           false -> ClipboardOnly, STOP (no restore)
 4. wait 50 ms                                 the reference's constant, kept
 5. simulate Ctrl+V / Cmd+V                    UioHookResult != Success -> ClipboardOnly, STOP
 6. wait 1000 ms                               cancellation shortens this; it never skips step 7
 7. restore previous                           only under the three guards below
                                               -> Pasted
```

The reason this is one service rather than the proposal's original `IClipboardService` +
`IPasteService` with change 8 sequencing them is that steps 5-7 are not a sequence, they are
invariants. "Never restore after a failed paste" and "only restore what is still ours" are
correctness rules about the pair, and a pipeline that can call the two services in any order is a
pipeline that can violate them. Keeping them here also keeps them testable: `TextOutput` is pure
orchestration over two interfaces, so every rule below is a unit test with no clipboard and no
keyboard.
*Alternative rejected:* two thin services, per the original proposal. It pushes the guards into
change 8, where they would have to be re-derived — and change 8 is the largest change in the roadmap
already.

**There is no `IPasteService` because SharpHook already is one.**
*From the assembly:* `SharpHook.Simulation.IEventSimulator` is a public interface, `EventSimulator`
is created through the static `EventSimulator.Create(string applicationName,
IEventSimulationProvider)`, and `SharpHook.Testing.TestProvider` — already the hook provider behind
change 6's tests — implements the simulation side too, recording `PostedEvents` and exposing a
settable `PostEventResult`. Wrapping that in a project-local interface would add an abstraction over
an abstraction whose only purpose is a test double that already exists. `TextOutput` takes
`IEventSimulator` directly; the per-platform parts of the keystroke live in a private method.
*Consequence worth having:* one `TestProvider` instance can back both the hotkey service and the
simulator in a single test, which is what proves that the application does not observe its own paste
as a hotkey.

**The clipboard is native code in `Pisum.Whisper.Platform`, not Avalonia.**
The proposal said Avalonia's `IClipboard` marshalled onto the UI thread. That is not available to
this application. *From the assembly:* `Avalonia.Application` in 12.1.1 has no `Clipboard` property
— 11.x's obsolete one is gone — the only public route to an `IClipboard` is `TopLevel.Clipboard`,
and the concrete `Avalonia.Input.Platform.Clipboard` that wraps an `IClipboardImpl` is not exported,
so it cannot be constructed. This process is tray-only and creates no `TopLevel` at all.

So `Pisum.Whisper.Core.Output.ISystemClipboard` is declared in Core and implemented in Platform,
which is exactly what that project is for and which this change is the first code to put in it — it
is empty today. `Platform` references `Core` one-way, so the interface has to live in Core in any
case; the only question was how much else went with it. The answer is: as little as possible. The
interop has no branches worth testing and the sequence logic has nothing but.

```
Core/Output/          ITextOutput, TextOutput, ISystemClipboard, IPasteProbe, TextOutputOutcome,
                      TextOutputException, TextOutputServiceCollectionExtensions
Platform/Output/      WindowsClipboard, MacOsClipboard, WindowsPasteProbe, MacOsPasteProbe,
                      NativeOutputServiceCollectionExtensions
App/Program.cs        services.AddTextOutput();    // Core: ITextOutput + IEventSimulator
                      services.AddNativeOutput();  // Platform: ISystemClipboard + IPasteProbe
```

Registering the two halves separately is deliberate: `ValidateOnBuild` is already on in
`Program.cs`, so forgetting the Platform half is a named startup failure rather than a null at first
paste. `AddNativeOutput` selects by `OperatingSystem.IsWindows()` / `IsMacOS()` and throws
`PlatformNotSupportedException` otherwise, matching the project's "runtime OS checks plus
`[SupportedOSPlatform]`" rule; only win-x64 and osx-arm64 are shipped.
*Alternatives rejected:* a hidden `Window` created solely to own an `IClipboard` — unproven that a
never-shown window yields a working one, and a window that ever surfaces or takes focus defeats the
entire feature, which depends on the *other* application keeping focus. And borrowing the settings
window's `TopLevel` from change 10 — dictation has to work with no window open, which is the normal
case.

**`ISystemClipboard` is synchronous.** `string? TryGetText()` and `void SetText(string text)`. Both
platform APIs are synchronous; the only thing that waits is Windows' `OpenClipboard` retry, bounded
below at about 100 ms, and it runs on change 8's background pipeline thread. `DeliverAsync` is async
because of the three sleeps, not because the clipboard is.
*Alternative rejected:* `Task`-returning members, which would be ceremony over synchronous native
calls.

**Three guards decide whether the restore happens at all.** All three are requirements, not
optimisations:

1. **Never restore after a failed paste.** The degraded outcome tells the user the transcript is on
   the clipboard and to press Ctrl+V themselves; putting the old contents back would make that a
   lie and lose the transcript outright. Step 5 returns immediately on a non-`Success` result, and
   so does step 3 when the probe says the paste cannot land.
2. **Only restore if the clipboard still holds our transcript.** Read it back and compare before
   writing. Transcription takes seconds; if the user copied something in the meantime, that copy is
   newer than anything we saved and must win. This also makes concurrent dictations safe — a second
   dictation's transcript is not ours, so the first one's pending restore stands down.
3. **Only text is restored.** `TryGetText` returning `null` — an empty clipboard, an image, a file
   list — means there is nothing to put back and the restore is skipped. The transcript stays.
   Round-tripping arbitrary formats is a non-goal, and the destruction already happened at step 2.

**A paste that cannot land is not attempted.** `Core/Output/IPasteProbe.cs` — `bool CanPaste()`,
implemented in Platform beside the clipboard, named for the `IGeminiKeyProbe` this codebase already
has — is consulted after the clipboard is written and before the keystroke is sent. `false` returns
`ClipboardOnly` with no events posted and, per guard 1, no restore.

This exists because the worst outcome in the whole change is otherwise undetectable. Synthetic input
into a higher-integrity window on Windows is dropped by the operating system with no error, and
`UioHookResult` reports `Success`; macOS drops it the same way without an Accessibility grant. The
sequence then waits, finds its own transcript still on the clipboard — guard 2 passes, because
nothing else touched it — restores the previous contents over it, and the user's speech is gone with
no message at all. The probe converts that into the degraded outcome that already exists, and the
manual Ctrl+V it asks for **works**, because the clipboard is shared even where synthetic input is
not.

- *macOS:* `AXIsProcessTrusted()` from ApplicationServices, one P/Invoke, the same check libuiohook
  makes internally. Definitive rather than heuristic.
- *Windows:* `GetForegroundWindow` → `GetWindowThreadProcessId` →
  `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`, and `ERROR_ACCESS_DENIED` means the target sits
  above us and cannot be reached. Heuristic — a protected process can deny us for other reasons —
  but the error direction is the safe one: a false negative costs the user a manual paste with their
  transcript intact, where a false positive is the silent loss this probe exists to prevent.

*Alternative rejected:* confirming the paste after the fact. Nothing on either platform reports that
another application consumed a clipboard read, which is the same wall the restore delay runs into.
*Note:* the Windows half is the weaker of the two and is a separable task; the macOS half is where a
failure is known to happen today.

**The transcript is trimmed of leading and trailing whitespace before it is delivered.**
*Measured against the code:* neither `GeminiProvider.ExtractText` nor the reference's `extract_text`
trims — both use a trimmed copy only to detect an empty response and then return the original — and
Gemini's `generateContent` routinely ends a text part with a newline. Left alone, a stray blank line
is pasted at the cursor on every single dictation.

The trim lives here rather than in change 5 because what reaches the cursor is this capability's
business and change 5 is complete; the transcript's *content* is the model's, its *presentation at
the insertion point* is ours. It is a deliberate divergence from the reference, in the same class as
the clipboard restore, and it is why step 0 can turn a non-empty argument into an
`ArgumentException`: text that is only whitespace has nothing left after trimming.
*Alternative rejected:* trimming in `GeminiProvider`, which is arguably the more honest home but
reopens a change sitting at 26 of 26 tasks to fix something that is only observable at the cursor.

**Cancellation never abandons the clipboard in a modified state.** Once step 2 has written the
transcript, the restore is owed. A cancelled token therefore shortens step 6 to nothing and runs
step 7 immediately rather than abandoning the sequence — the guards still apply, so a cancelled
delivery restores only what it would have restored anyway.

Without this there is a second-long window in which the transcript is on the clipboard and the
user's own text exists only in a local variable, and quitting inside that window destroys it
permanently — with the native Windows write, the transcript then outlives the process that put it
there, because `SetClipboardData` hands ownership to the clipboard. That is precisely the harm this
change exists to prevent, caused by the mechanism meant to prevent it. It follows that change 8 must
await a delivery in progress during `StopAsync` rather than dropping it; a note for that design, and
the reason `DeliverAsync` takes a token at all.

**Deliveries are serialised, because the guards do not compose with themselves.** `TextOutput` holds
a `SemaphoreSlim(1, 1)` around the whole sequence, so a second delivery waits for the first one's
restore. Every guard behaves exactly as specified in the interleaving below and the result is still
wrong:

```
user clipboard = "USER"

A: prev = "USER"           writes "alpha"   pastes
B:             prev = "alpha"  writes "beta"  pastes
A: wakes, clipboard is "beta" != "alpha" -> guard 2, restore skipped
B: wakes, clipboard is "beta" == "beta"  -> restores "alpha"

clipboard ends as "alpha", a transcript; "USER" is gone from everywhere
```

The defect is that B's step 1 reads A's transcript and takes it for the user's clipboard. No guard
can see that, because from B's position it is indistinguishable from the user having copied
something — and the loss is silent and permanent. The gate costs three lines and, in practice, no
waiting at all: two deliveries a second apart require two transcriptions finishing a second apart,
which the pipeline guard below already prevents.
*Alternative rejected:* relying on change 8 alone. The reference refuses to start a recording while
one is transcribing (`manager.rs:204`, notifying "Transcription In Progress"), and change 8 SHOULD
do the same — but this design's whole argument for one service was that these invariants do not
belong at the call site, and change 8 is not written yet.

**The restore delay is 1000 ms, and the number is chosen from the cost of being wrong.**
Nothing tells us when the target application has read the clipboard: it reads it synchronously while
processing the injected keystroke, at whatever moment its message loop gets there, and a busy
Electron or browser window can take hundreds of milliseconds. Restoring too early does not merely
lose the paste — it makes the target paste **the previous clipboard contents**, which are as likely
as not the password the user copied out of a password manager a minute ago, into whatever document
they were typing in. Restoring too late costs nothing but a second of an invisible tail, guarded by
rule 2 against anything the user does in the meantime. The asymmetry is total, so the delay is
generous.
*Alternative rejected:* Windows delayed rendering — `SetClipboardData(fmt, NULL)` and answer
`WM_RENDERFORMAT` when the target actually asks — which would make the moment exact instead of
guessed. It needs a message-only window and a pump inside a process that deliberately has neither,
it has no macOS counterpart, and it converts a one-line constant into a platform-specific subsystem.

**macOS attempts the paste exactly as Windows does.** The only per-platform differences are the
modifier keycode (`VcLeftMeta` rather than `VcLeftControl`) and a 30 ms pause after every simulated
edge, which *from the spikes* is required on macOS: `EventSimulator` posting edges back-to-back
outruns the OS folding earlier keys into the modifier flags, so Cmd+V arrives as a bare "v".
Windows needs no pacing and gets none.

This is a decision against the matrix's documented fallback, and the reason is that S1b's macOS
failure is undiagnosed rather than fundamental. The same run found the hook observing zero events on
the first launch after every rebuild, and its root cause — an unsigned binary whose Accessibility
grant does not survive a rebuild, task 1.4 — is a build-environment defect that packaging fixes. The
grant that lets this process observe the hotkey is the same grant that lets it post events, so a
process that got as far as a dictation is one whose paste is expected to work. Hard-coding
"macOS never pastes" would bake a workaround for an undiagnosed bug into the product and would have
to be unwound the moment change 12 signs the app.
*Alternative rejected:* clipboard-only output on macOS, per the matrix's fallback. Kept in reserve:
if a signed build still fails S1b, the change is one branch in `TextOutput`, and the degraded
outcome and its message already exist.
*Also rejected:* `IEventSimulator.SimulateTextEntry`, which types the transcript directly and needs
no clipboard at all. It is slow for a paragraph, interleaves with anything the user types, and — the
decisive part — posts through the same mechanism the paste does, so it fails wherever the paste
fails.

**The macOS clipboard is NSPasteboard through the Objective-C runtime, not `pbcopy`/`pbpaste`.**
Five selectors (`generalPasteboard`, `clearContents`, `setString:forType:`, `stringForType:`,
`types`) through `objc_getClass` / `sel_registerName` / `objc_msgSend`, with a `dlopen` of AppKit
first so the class is present regardless of what else has been loaded. The spike's `pbcopy`/`pbpaste`
route works and was right for a spike, but it costs three process launches per dictation, cannot
inspect what is on the pasteboard, and cannot set the concealed-type hint discussed under Open
Questions.
*Alternative rejected:* `pbcopy`/`pbpaste` via `Process.Start`. Kept as the fallback if the interop
proves troublesome — it is proven to work and is a twenty-line class.

**The Windows clipboard is the plain Win32 API, and `OpenClipboard` is retried.**
`OpenClipboard(IntPtr.Zero)` fails with `ERROR_ACCESS_DENIED` whenever another process holds the
clipboard, which on a normal desktop happens routinely; ten attempts at 10 ms apart before giving up
is required behaviour, not an implementation detail. Then `EmptyClipboard`, a `GlobalAlloc`
(`GMEM_MOVEABLE`) copy of the UTF-16 text, `SetClipboardData(CF_UNICODETEXT, handle)` — and the
handle is *not* freed afterwards on success, because the system owns it — with `CloseClipboard` in a
`finally`. Reads check `IsClipboardFormatAvailable(CF_UNICODETEXT)` first.

Ownership is also why this route is better than the Avalonia one it replaces, beyond the fact that
Avalonia's is unavailable: `SetClipboardData` hands the data to the clipboard, so it survives the
process exiting. Avalonia's OLE path leaves it owned by the process and needs `FlushAsync` to
survive — which matters precisely in the degraded case, where the transcript is sitting on the
clipboard waiting for a user who may quit the app before pasting.

**The transcript is written so the operating system does not keep it.** Left alone, every dictated
sentence lands in Windows' Win+V history — and, if the user has it switched on, in the clipboard
sync tied to their Microsoft account — and in whatever clipboard manager a Mac user runs. That is
the user's speech, retained indefinitely, by a product whose own logging rules forbid writing a
transcript to a file we control. The native route can opt out where Avalonia's could not: on Windows
the write additionally sets the documented `CanIncludeInClipboardHistory` format to 0 and the
`ExcludeClipboardContentFromMonitorProcessing` format; on macOS it adds the
`org.nspasteboard.ConcealedType` type that clipboard managers honour by convention. The same
exclusion covers the restore write, so the user's own previous clipboard text is not duplicated into
history either.

Neither mechanism is verified here — the Windows formats are documented behaviour and the macOS one
is a community convention rather than an API — so confirming both on a real machine is a task of
this change, not an assumption of it. If a mechanism turns out not to work, the transcript is
retained by that platform's history and nothing else about the change breaks.
*Trade-off accepted:* a user who wants a dictation back after the restore has taken it off the
clipboard cannot find it in Win+V. That is consistent with "no transcript history" already being a
non-goal, and re-dictating is cheap.

**A hard failure throws; a degraded success is a value.** `TextOutputOutcome` is `Pasted` or
`ClipboardOnly`, and `TextOutputException` — one type for the capability, message written to be
shown to the user as-is — carries the case where the clipboard could not be written at all, which is
the only outcome where the user's transcript is genuinely lost. That split mirrors the existing
`AudioException` / `SettingsException` / `TranscriptionException` shape, and it keeps
`ErrorCategory`'s deliberate omission of an `Output` member correct: this capability's distinctions
are carried by its own type, exactly as the comment on that enum says.

A transcript with nothing left after step 0's trim is an `ArgumentException`. Gemini returning
nothing usable is already `ErrorCategory.Transcription` on change 5's side, so a blank string
reaching here is a programming error rather than a runtime condition.

**Nothing touches the UI thread, and nothing runs on the hook thread.** Dropping Avalonia's
clipboard removes the only reason this capability would have needed `Dispatcher.UIThread`, so
`TextOutput` is thread-agnostic and change 8 calls it from its background pipeline task. It must
never be called from a hook handler: it sleeps for more than a second, and *from the spikes* both
platforms police that thread — Windows silently removes a hook that exceeds `LowLevelHooksTimeout`.

**The logging rules extend to clipboard contents.** Change 3's rule is never to log transcript text;
the previous clipboard contents are the same class of data and worse, since a password manager's
clipboard is the obvious thing to find there. Neither is ever logged, at any level, and the previous
contents are not logged even by length. What is logged: the transcript's character count, the paste
result, and which of the three guards stopped a restore.

**Verification.** `TextOutput` is unit-tested in `Core.Tests` over fake `ISystemClipboard` and
`IPasteProbe` implementations and a `TestProvider`, covering each guard, both degraded outcomes (the
probe refusing, and `PostEventResult`), the trim, cancellation mid-sequence, the keystroke shape per
platform, and that the hotkey service does not observe the paste. The interop gets a new
`tests/Pisum.Whisper.Platform.Tests` project holding a clipboard round-trip that runs by name only,
following the `ManualCaptureSmokeTest` / `ManualTranscriptionSmokeTest` precedent — a real clipboard
is not something a CI agent reliably has.
*Alternative rejected:* adding a `ProjectReference` to Platform from `Core.Tests`, which is cheaper
by one project and makes the tests for the layer that has no platform dependencies depend on the
platform layer.

## Risks / Trade-offs

**A paste into an elevated window on Windows is silently dropped.** `SendInput` cannot reach a
higher-integrity process from a non-elevated one, and it reports success; guard 2 does not help,
because the clipboard genuinely still holds our text. → `IPasteProbe` catches the common case before
the write is wasted, degrading to `ClipboardOnly` and a manual Ctrl+V that works. What the probe
cannot catch — a target that denies `OpenProcess` for a reason unrelated to integrity, or accepts it
and still drops the input — remains a silent loss, and is the residual risk this change accepts.

**A silently-failed paste on macOS looks identical.** `UioHookResult` reports what was posted, not
what was accepted. → `AXIsProcessTrusted()` covers the known cause, a missing Accessibility grant.
Whatever made spike S1b fail with the grant present is not covered by it, which is why the S1b row
keeps its fallback in reserve rather than being closed by this change.

**The user may be holding a modifier when the paste is sent.** A hand resting on Shift turns our
Ctrl+V into Ctrl+Shift+V — paste-as-plain-text in some applications, nothing in others. → Inherent
to synthetic input and present in the reference; not mitigated. The hotkey's own keys are not a
concern: in hold-to-record the binding is released before transcription starts.

**A clipboard manager can defeat the restore.** Managers that write their own history entry, or that
re-assert the last copied item, will see three writes per dictation. → Guard 2 keeps us from
fighting them: if the clipboard no longer holds our transcript, we stand down.

**The restore adds about a second to the tail of `DeliverAsync`.** The text is already in the user's
document when it starts, so the delay is invisible unless change 8 ties a user-visible state to the
call completing. → Change 8 should return the tray to idle on the outcome rather than on the tail if
that proves visible; the call stays awaited either way so a failed restore is still logged.

**The interop is the first code in `Pisum.Whisper.Platform` and sets its precedent.** → Kept
deliberately thin: two classes, no branching logic, one registration extension, and a manual
round-trip test. Everything with a decision in it stays in Core.

## Open Questions

**Do the history-exclusion mechanisms actually work?** The decision to exclude is made; what is
unverified is that Windows' `CanIncludeInClipboardHistory` / `ExcludeClipboardContentFromMonitorProcessing`
formats and macOS' `org.nspasteboard.ConcealedType` convention have the effect they document. Both
are checked by hand — copy a dictation, then look in Win+V and in a clipboard manager — as a task of
this change. *macOS half answered 2026-09-02, see Verification results:* `ConcealedType` keeps a
`MacOsClipboard.SetText` write out of a real running clipboard manager's history, verified against
Maccy with a sentinel proving the manager was actually watching. The Windows half (Win+V) remains
unmeasured — 3.3's Windows run passed only vacuously, since clipboard history was off on that
machine.

**Is 1000 ms right?** *Answered for Windows, see Verification results:* a cold Edge window and
Notepad both read the clipboard within 50 ms, so the constant is kept with a twentyfold margin. An
Office document, a terminal, and everything on macOS are still unmeasured.

**Does S1b pass on macOS once the app is signed?** Task 1.4 owns the signing identity; until it is
done, the macOS half of this change cannot be verified end to end and the `ClipboardOnly` outcome is
what a macOS user will get. This change does not close that row of the platform verification matrix.

## Verification results

Run on 2026-08-31 on win-x64 (Windows 11 Pro 10.0.26200) through a throwaway harness in the
scratchpad that drives the real `WindowsClipboard`, `WindowsPasteProbe`, `TextOutput` and
`EventSimulator` against real windows. **No macOS run had happened at the time**, so every osx-arm64
row below was open; task 3.5 is closed below, tasks 3.6 and 5.4 remain unticked here (recorded on
separate not-yet-merged PRs, #40 and #39).

| # | What was checked | Result |
|---|---|---|
| 3.2 | `OpenClipboard` retried while a second process hammers `Set-Clipboard` | **PASS** — 206 write+read pairs against the contending process, 0 failures |
| 5.3 | Full delivery into Notepad, normal window | **PASS** — outcome `Pasted`, transcript at the cursor, the known clipboard text back afterwards, `DeliverAsync` 1097 ms end to end |
| 3.7 | `WindowsPasteProbe` against a normal foreground window | **PASS** — Notepad answers `true` |
| 5.5 | Shortest delay after which overwriting the clipboard no longer corrupts the paste | Notepad **≤50 ms**, cold Edge window (address bar) **≤50 ms** — 2/2 trials at the smallest delay tried |

**1000 ms stands, and is now a measured margin rather than a guess.** The two targets available here
read the clipboard within 50 ms, so the constant carries roughly a twentyfold margin. The asymmetry
that chose it is unchanged — being early makes the target paste the user's previous clipboard
contents into their document — so the evidence does not demand an adjustment. An Office document and
a terminal were not measured: neither is installed on this machine, and a terminal has no
select-all/copy read-back that this harness can use.

**Three checks could not be performed here, and none of them is a failure.**

- *The negative branch of `IPasteProbe` on Windows (part of 3.7, and the elevated half of 5.3).* The
  interactive session on this machine is elevated, so there is no higher-integrity window for
  `OpenProcess` to be denied by: Task Manager in the foreground correctly answers `true`, because
  from an elevated process it genuinely is reachable. The branch needs a non-elevated run against an
  elevated window.
- *Clipboard history exclusion (3.3).* `HKCU\Software\Microsoft\Clipboard\EnableClipboardHistory` is
  not set on this machine, so Win+V retains nothing from any application and the check would pass
  vacuously. It needs a machine with clipboard history switched on.
- *Everything macOS except 3.5 (3.6, 5.4).* No Apple Silicon host was available to this run; 3.5 is
  covered by the macOS run below.

### macOS run — 2026-09-02 (issue #31, task 7/3.5)

Run on an Apple M4 (macOS 26.6.2), against Maccy (`org.p0deje.Maccy`), a real clipboard manager
already running with Accessibility granted. A throwaway harness referenced `Pisum.Whisper.Platform`
directly and called `new MacOsClipboard().SetText(token)` — the same call `TextOutput` makes at step 2
of `DeliverAsync` — with no app, no DI, no dictation. Maccy's own history store (a Core Data SQLite
database under its sandbox container) was queried directly rather than read from its menu.

A sentinel makes the negative result meaningful: an unmarked write (`pbcopy`, no `ConcealedType`) was
made first as a positive control, to prove Maccy is actually capturing this session's clipboard writes
before trusting an absence.

| # | What was checked | Result |
|---|---|---|
| — | Positive control: unmarked token via `pbcopy` | **Captured** — appeared in Maccy's history within 2 s, confirming the harness is live |
| 3.5 | Marked token via the real `MacOsClipboard.SetText` (`ConcealedType` applied) | **PASS** — never appeared in Maccy's history, even after a longer wait; `pbpaste` confirmed the token genuinely was on the clipboard throughout |

**A stale-looking result in the same database is not a counter-example, and is worth writing down so
nobody re-diagnoses it as one.** Querying the same table for older entries turns up several
`PISUM-7.5.4-…` tokens from task 7/5.4's own verification harness (PR #39), attributed to
`com.apple.TextEdit` rather than to this application. Those came from *that* harness's independent
check — selecting all and copying back out of TextEdit to confirm the paste landed — and TextEdit's
own `Cmd+C` carries no `ConcealedType` marking of its own, so that re-copy is a legitimately separate,
unmarked write Maccy was right to capture. `ConcealedType` protects the write this application makes;
it says nothing about, and cannot reach, a later copy the user (or a verification harness) makes from
wherever the transcript landed.
