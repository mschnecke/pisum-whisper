## MODIFIED Requirements

### Requirement: A transcription is bounded by a total budget
The application SHALL abandon a transcription that has not completed within 120 seconds, counted
across every retry and every configured provider, and SHALL report it as a failure. The per-request
timeout does not bound a transcription: retries and provider fallback multiply it, so a hung
connection would otherwise hold the application unable to start another dictation for several
minutes with nothing said.

The budget SHALL be large enough that a recording of the maximum length the settings allow,
transcribed over an ordinary connection, completes inside it with margin to spare. A budget that
abandons the longest legitimate dictation is a defect of the budget, not of the connection. The
number was chosen from the shape of the retry policy; it is confirmed, or moved once, against a
timed maximum-length recording rather than derived.

#### Scenario: A transcription hangs
- **WHEN** a transcription has not produced a result within the budget
- **THEN** it is abandoned, a failure is described, and the application returns to idle

#### Scenario: The longest recording completes within the budget
- **WHEN** a recording at the maximum configured duration is transcribed over an ordinary connection
- **THEN** the transcript is delivered inside the budget rather than abandoned
