## ADDED Requirements

### Requirement: Holding the hotkey records and delivers a transcript
The application SHALL, when the recording mode is hold-to-record, begin recording when the configured
hotkey becomes held and stop recording when it stops being held, then encode the recording, transcribe
it, and deliver the transcript to the cursor of the focused application. This is the whole product in
one sentence; every other requirement here constrains it.

#### Scenario: A dictation is spoken and delivered
- **WHEN** the hotkey is held, speech is captured for longer than the minimum duration, and the hotkey is released
- **THEN** the recording is encoded, transcribed, and the resulting text is delivered at the cursor

#### Scenario: The hotkey is released while the pipeline is configured but unreachable
- **WHEN** a dictation is spoken and the transcription cannot be produced
- **THEN** nothing is delivered, the failure is described, and the application returns to idle

### Requirement: Toggling the hotkey starts and stops a recording
The application SHALL, when the recording mode is toggle, start recording on a hotkey press when idle
and stop recording on a hotkey press while recording, and SHALL ignore the hotkey release entirely.

#### Scenario: The first press starts recording
- **WHEN** the hotkey is pressed in toggle mode while the application is idle
- **THEN** recording starts

#### Scenario: The hotkey release is ignored
- **WHEN** the hotkey is released in toggle mode while recording
- **THEN** recording continues

#### Scenario: A later press stops recording
- **WHEN** the hotkey is pressed again in toggle mode while recording
- **THEN** recording stops and the dictation proceeds to transcription

### Requirement: A recording shorter than the minimum duration is discarded silently
The application SHALL discard a recording that lasted less than 50 milliseconds, without transcribing
it and without reporting anything to the user. An accidental brush of the hotkey must do nothing at
all, which means it must not produce an error either.

#### Scenario: The hotkey is brushed
- **WHEN** the hotkey is held for less than 50 milliseconds and released
- **THEN** the capture is stopped, the samples are discarded, no transcription is attempted, and nothing is reported

#### Scenario: A short but deliberate press
- **WHEN** the hotkey is held for longer than 50 milliseconds
- **THEN** the recording proceeds to transcription like any other

### Requirement: A recording that captured no audio is reported as a fault
The application SHALL report a recording error when a recording lasted at least the minimum duration
but produced no samples. Having already ruled out an accidental brush by elapsed time, an empty
capture means the input device produced nothing — a muted, disconnected or misrouted microphone —
which the user cannot diagnose from silence.

#### Scenario: The microphone produces nothing
- **WHEN** the hotkey is held for well over the minimum duration and the capture returns no samples
- **THEN** no transcription is attempted and a recording error is described saying no audio was recorded

#### Scenario: An empty capture below the minimum duration
- **WHEN** a capture returns no samples and the recording lasted less than the minimum duration
- **THEN** it is discarded silently as a brush, and no error is described

### Requirement: Repeated presses inside the toggle debounce window are ignored
The application SHALL ignore a hotkey press in toggle mode that arrives within 200 milliseconds of the
previous press. Without it, a fumbled double-tap starts and stops a recording of a fraction of a
second, which is long enough to escape the minimum-duration discard and so reaches transcription and
fails there.

#### Scenario: A rapid double-tap
- **WHEN** the hotkey is pressed twice in toggle mode within 200 milliseconds
- **THEN** the second press is ignored and the recording started by the first press continues

#### Scenario: A deliberate second press
- **WHEN** the hotkey is pressed again in toggle mode more than 200 milliseconds after the previous press
- **THEN** the press is acted on

### Requirement: Only one dictation runs at a time
The application SHALL refuse to start a second dictation while one is in progress, and SHALL
distinguish the two cases: a hotkey press while already recording is ignored without comment, and a
hotkey press while a transcription is in flight is reported to the user as a transcription already
being in progress.

#### Scenario: A press while already recording
- **WHEN** the hotkey is pressed while a recording is in progress in a mode where that does not stop it
- **THEN** nothing happens and nothing is reported

#### Scenario: A press while transcribing
- **WHEN** the hotkey is pressed while a transcription is in flight
- **THEN** no recording starts and the user is told a transcription is already in progress

#### Scenario: A press after a dictation completes
- **WHEN** the hotkey is pressed after the previous dictation has finished, however it finished
- **THEN** a new recording starts

### Requirement: A recording is stopped automatically at the configured maximum duration
The application SHALL stop a recording that reaches the configured maximum duration, transcribe what
was captured rather than discarding it, and tell the user why the recording ended. The watchdog SHALL
be cancelled when a recording ends normally, so that a completed dictation leaves nothing running.

#### Scenario: A recording reaches the maximum duration
- **WHEN** a recording runs for the configured maximum duration without being stopped
- **THEN** recording stops, the captured audio is transcribed and delivered, and the user is told the recording was stopped automatically

#### Scenario: A recording ends before the maximum
- **WHEN** a recording is stopped by the hotkey before the maximum duration
- **THEN** the watchdog is cancelled and does not fire afterwards

#### Scenario: The watchdog and a hotkey stop coincide
- **WHEN** the maximum duration is reached at the same moment the hotkey stops the recording
- **THEN** the recording is stopped and transcribed exactly once

### Requirement: The dictation state is reported to the rest of the application
The application SHALL expose whether it is idle, recording, or transcribing, and SHALL raise a
notification whenever that value changes. Recording and transcribing are reported separately because
they ask different things of the user — one means keep speaking, the other means stop — and because
the two states answer a hotkey press differently.

#### Scenario: Recording begins
- **WHEN** a recording starts
- **THEN** the state becomes recording and the change is announced

#### Scenario: Recording ends and transcription begins
- **WHEN** a recording is stopped and will be transcribed
- **THEN** the state becomes transcribing at the moment the recording is claimed, not when the capture device has finished closing

#### Scenario: A dictation finishes
- **WHEN** a dictation completes, whether it succeeded or failed
- **THEN** the state returns to idle and the change is announced

#### Scenario: A recording is discarded as too short
- **WHEN** a recording is discarded for being shorter than the minimum duration
- **THEN** the state returns to idle directly and transcribing is never announced

### Requirement: A transcription is bounded by a total budget
The application SHALL abandon a transcription that has not completed within 120 seconds, counted
across every retry and every configured provider, and SHALL report it as a failure. The per-request
timeout does not bound a transcription: retries and provider fallback multiply it, so a hung
connection would otherwise hold the application unable to start another dictation for several
minutes with nothing said.

#### Scenario: A transcription hangs
- **WHEN** a transcription has not produced a result within the budget
- **THEN** it is abandoned, a failure is described, and the application returns to idle

#### Scenario: A transcription completes inside the budget
- **WHEN** a transcription completes before the budget expires
- **THEN** the budget has no effect on it

#### Scenario: Providers fail quickly
- **WHEN** each configured provider fails fast, for example by rejecting the API key
- **THEN** every provider is still tried, because the budget bounds elapsed time rather than the number of attempts

### Requirement: A dictation is transcribed under the active preset's system prompt
The application SHALL transcribe each recording using the system prompt of the preset that is active
when the recording is transcribed, read at that moment rather than cached when the application
started.

#### Scenario: A dictation is transcribed
- **WHEN** a recording is sent for transcription
- **THEN** the active preset's system prompt accompanies it

#### Scenario: The active preset changes between dictations
- **WHEN** the active preset is changed and another dictation is spoken
- **THEN** the new preset's system prompt is used

### Requirement: Audio is encoded in the configured format at the capture sample rate
The application SHALL encode each recording in the format selected in settings, at the sample rate
the capture was requested at, and SHALL send whichever format the encoder actually produced. The rate
is fixed by the audio-capture capability rather than discovered per recording, and a wrong value is
not a visible failure — it makes every encode fall back to the other format silently.

#### Scenario: A recording is encoded
- **WHEN** a recording is prepared for transcription
- **THEN** it is encoded at the rate capture was requested at, in the configured format

#### Scenario: The preferred format cannot be encoded
- **WHEN** the configured format fails to encode and the encoder falls back to the other one
- **THEN** the recording is sent with the media type of the format that was actually produced

### Requirement: Every failure is described with a title and a message
The application SHALL turn any failure of a dictation into a short title and a message written to be
shown to the user as-is, choosing the title from what failed rather than by matching the text of an
error message. Recording, configuration, network, authentication, rate-limit, transcription and
output failures SHALL each be distinguishable, and a failure of no recognised kind SHALL still
produce a description rather than nothing.

#### Scenario: Transcription is refused for the API key
- **WHEN** a dictation fails because the configured key was rejected
- **THEN** the failure is described as an authentication failure, not as a generic one

#### Scenario: The clipboard cannot be written
- **WHEN** a dictation fails because the transcript could not be placed on the clipboard
- **THEN** the failure is described as an output failure

#### Scenario: An unrecognised failure
- **WHEN** a dictation fails in a way the application does not recognise
- **THEN** it is still described, as an unexpected failure pointing the user at the log

#### Scenario: The user quits during a dictation
- **WHEN** a dictation is abandoned because the application is shutting down
- **THEN** no failure is described, because the user asked for it

### Requirement: A failure never prevents the next dictation
The application SHALL return to idle after every dictation, including one that failed in a way nobody
anticipated, so that the hotkey works again immediately. A dictation that ends without resetting the
state leaves the hotkey answering that a transcription is in progress until the application is
restarted.

#### Scenario: The pipeline throws unexpectedly
- **WHEN** an unanticipated error occurs anywhere between stopping the recording and delivering the transcript
- **THEN** the failure is described, the state returns to idle, and the next hotkey press starts a new recording

#### Scenario: Delivery fails
- **WHEN** the transcript cannot be delivered
- **THEN** the state returns to idle and the next hotkey press starts a new recording

### Requirement: A transcript that was copied but not pasted is not a failure
The application SHALL treat a delivery that left the transcript on the clipboard without pasting it
as a successful dictation that needs a word to the user, not as an error. The user's speech survived;
what they lost is one keystroke they can perform themselves.

#### Scenario: The paste could not be sent
- **WHEN** a delivery reports that the transcript is on the clipboard and was not pasted
- **THEN** no failure is described, and the user is told the text can be pasted manually

#### Scenario: The paste succeeded
- **WHEN** a delivery reports that the transcript was pasted
- **THEN** nothing is reported to the user

### Requirement: The hotkey stays responsive while a dictation is in progress
The application SHALL NOT perform capture, encoding, transcription or delivery on the thread that
reports hotkey edges. Recording, transcribing and pasting take seconds, and a hotkey edge that cannot
be reported until they finish means the release that ends a hold-to-record dictation never arrives.

#### Scenario: A dictation is in progress
- **WHEN** a transcription and delivery are running
- **THEN** hotkey edges continue to be reported as they occur

#### Scenario: Hold-to-record
- **WHEN** the hotkey is released while the recording it started is still being stopped
- **THEN** the release is observed and ends the recording

### Requirement: Shutting down cancels a dictation in progress and waits for it
The application SHALL, when it is shutting down, stop a recording in progress and discard it, cancel a
transcription in progress, and wait for a delivery in progress to finish rather than abandoning it.
Between placing a transcript on the clipboard and restoring what was there before, the user's previous
clipboard contents exist nowhere else, so exiting inside that window destroys them permanently.

#### Scenario: Quitting while recording
- **WHEN** the application is shut down while a recording is in progress
- **THEN** the capture is stopped, the samples are discarded, and no transcription is started

#### Scenario: Quitting while transcribing
- **WHEN** the application is shut down while a transcription is in flight
- **THEN** the transcription is abandoned and shutdown does not wait for the full transcription budget

#### Scenario: Quitting while delivering
- **WHEN** the application is shut down while a transcript is being delivered
- **THEN** shutdown waits for the delivery to complete its clipboard restore before the process exits

### Requirement: Dictation never writes transcript text to the log
The application SHALL NOT write the text of a transcript to the log at any level. It MAY record the
transcript's length, the state transitions, the reason a recording was discarded, elapsed timings and
the description of a failure.

#### Scenario: A dictation is delivered with logging at its most verbose
- **WHEN** a dictation completes with the log level set to its most verbose setting
- **THEN** no logged message contains the transcript text, and the character count and outcome are present

#### Scenario: A dictation fails
- **WHEN** a dictation fails after a transcript was produced
- **THEN** the described failure is logged without the transcript text
