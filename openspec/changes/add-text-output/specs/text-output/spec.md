## ADDED Requirements

### Requirement: A transcript is delivered at the cursor of the focused application
The application SHALL deliver a transcript into whichever application holds keyboard focus, at its
cursor position, by placing the text on the system clipboard and sending that platform's paste
keystroke. Neither target platform offers a way to insert text into another application directly,
which is why the clipboard is involved at all.

#### Scenario: Another application has focus
- **WHEN** a transcript is delivered while a different application holds keyboard focus
- **THEN** the text is placed on the clipboard and a paste keystroke is sent, so the text appears at that application's cursor

#### Scenario: The application has no window
- **WHEN** a transcript is delivered while the application is running with no window shown
- **THEN** the delivery proceeds, because it needs no window of its own

### Requirement: Delivery never takes focus
The application SHALL NOT show a window, create a visible window, or otherwise change which
application holds keyboard focus in order to deliver a transcript. The text is pasted into the
application the user was already typing in, so anything that takes focus defeats the delivery it was
meant to perform.

#### Scenario: A transcript is delivered
- **WHEN** a transcript is delivered
- **THEN** the application that held keyboard focus before the delivery still holds it, and no window of this application is shown

### Requirement: The paste keystroke matches the platform
The application SHALL send Ctrl+V on Windows and Cmd+V on macOS, pressing the modifier, pressing and
releasing V, then releasing the modifier. On macOS the simulated edges SHALL be paced apart, because
edges posted back to back outrun the operating system folding earlier keys into the modifier flags
and the paste arrives as a bare "v".

#### Scenario: Delivering on Windows
- **WHEN** a transcript is delivered on Windows
- **THEN** the simulated keystroke is Ctrl held, V pressed and released, Ctrl released

#### Scenario: Delivering on macOS
- **WHEN** a transcript is delivered on macOS
- **THEN** the simulated keystroke is Cmd held, V pressed and released, Cmd released, with a pause between each edge

#### Scenario: A platform whose paste has previously failed
- **WHEN** a transcript is delivered on a platform where the paste keystroke is known to have failed before
- **THEN** the paste is still attempted, and the outcome decides what the user is told

### Requirement: The application does not observe its own paste
The application SHALL NOT treat the paste keystroke it sends as a hotkey press or release. The
global hook is live throughout a dictation and observes injected events on the same path as physical
ones, so without this a delivery could start another dictation.

#### Scenario: The paste keystroke is sent while the hook is running
- **WHEN** the paste keystroke is simulated while the global hotkey hook is observing
- **THEN** no hotkey press and no hotkey release is reported

### Requirement: A paste the platform will drop is not attempted
The application SHALL determine, before sending the paste keystroke, whether synthetic input can
reach the application that holds focus, and SHALL skip the keystroke and report the transcript as
copied-but-not-pasted when it cannot. Both platforms discard injected input silently and report
success — Windows for a window of higher integrity than this process, macOS without an Accessibility
grant — so a delivery that does not check first destroys the transcript with no message.

#### Scenario: The focused window cannot receive synthetic input
- **WHEN** a transcript is delivered while the focused application cannot be reached by synthetic input
- **THEN** no paste keystroke is sent, the transcript is left on the clipboard, nothing is restored over it, and the outcome reports that the text was copied and can be pasted manually

#### Scenario: The focused window can receive synthetic input
- **WHEN** a transcript is delivered while the focused application can be reached
- **THEN** the paste keystroke is sent as normal

#### Scenario: A manual paste after the check refused
- **WHEN** the user presses the paste combination themselves after being told the paste could not be sent
- **THEN** the transcript is pasted, because the clipboard is reachable where synthetic input is not

### Requirement: The transcript is delivered without surrounding whitespace
The application SHALL remove leading and trailing whitespace from the transcript before placing it
on the clipboard. Transcription returns the model's response verbatim, and that response routinely
ends in a newline, which would otherwise be pasted into the user's document on every dictation.

#### Scenario: A transcript ending in a newline
- **WHEN** a transcript that ends with a newline is delivered
- **THEN** the text placed on the clipboard ends with the last character of the transcript itself

#### Scenario: A transcript with internal line breaks
- **WHEN** a transcript containing line breaks between words is delivered
- **THEN** those line breaks are preserved and only the surrounding whitespace is removed

### Requirement: A cancelled delivery still restores the clipboard
The application SHALL complete the restore when a delivery is cancelled after the transcript has
been written, shortening the wait rather than abandoning the sequence. Between the write and the
restore, the user's previous clipboard contents exist nowhere but in the delivery itself, and on
Windows the transcript outlives the process that wrote it — so a delivery abandoned in that window
destroys the user's clipboard permanently.

#### Scenario: Cancellation between the paste and the restore
- **WHEN** a delivery is cancelled after the paste keystroke has been sent
- **THEN** the restore is performed immediately under the same guards, rather than being skipped

#### Scenario: Cancellation before the transcript is written
- **WHEN** a delivery is cancelled before the transcript has been placed on the clipboard
- **THEN** the delivery stops with the clipboard unchanged

### Requirement: A failed paste leaves the transcript on the clipboard
The application SHALL report that the transcript was copied but not pasted, and SHALL leave the
transcript on the clipboard, when the paste keystroke cannot be sent. The result is the product of
everything the user just did; losing it because one keystroke failed is worse than asking them to
press Ctrl+V.

#### Scenario: The paste keystroke fails
- **WHEN** the paste keystroke cannot be sent
- **THEN** the transcript remains on the clipboard, nothing is restored over it, and the outcome reports that the text was copied to the clipboard but the paste failed and can be performed manually

### Requirement: The previous clipboard text is restored after a successful paste
The application SHALL read the clipboard's text before writing the transcript and SHALL put it back
after the paste, so that a dictation does not destroy whatever the user had copied. The restore
SHALL happen only after the focused application has had time to read the clipboard, because a
restore that lands first causes that application to paste the previous contents instead of the
transcript.

#### Scenario: A dictation over existing clipboard text
- **WHEN** the user has text on the clipboard, dictates, and the paste succeeds
- **THEN** the transcript is pasted into the focused application and the clipboard holds the user's text again afterwards

#### Scenario: Reading the previous contents fails
- **WHEN** the clipboard's existing contents cannot be read
- **THEN** the delivery continues with nothing to restore, rather than failing

### Requirement: The restore never overwrites newer clipboard contents
The application SHALL restore the previous contents only if the clipboard still holds the transcript
it wrote. Transcription takes seconds, during which the user may copy something; that copy is newer
than anything saved at the start of the delivery and SHALL win.

#### Scenario: The user copies something during the dictation
- **WHEN** the clipboard has been changed by anything other than this delivery between the paste and the restore
- **THEN** the restore is skipped and the newer contents are left alone

#### Scenario: A second dictation is delivered first
- **WHEN** a later delivery has replaced the clipboard contents with its own transcript
- **THEN** the earlier delivery's restore is skipped

### Requirement: Deliveries do not overlap
The application SHALL complete one delivery before beginning the next. Two deliveries in flight at
once defeat the restore guards rather than tripping them: the second reads the first one's
transcript and mistakes it for the user's clipboard, then faithfully restores a transcript over the
user's contents, which are by then held nowhere at all.

#### Scenario: A second delivery begins while one is in progress
- **WHEN** a delivery is requested while another has written its transcript and not yet restored
- **THEN** the second delivery waits for the first to finish before reading the clipboard

#### Scenario: Two deliveries in sequence
- **WHEN** two deliveries are performed one after another over the same starting clipboard contents
- **THEN** those contents are what remains on the clipboard at the end

### Requirement: Only text is restored
The application SHALL restore the previous clipboard contents only when they were text. An image, a
file list or an empty clipboard SHALL be treated as nothing to restore, and the transcript is left
in place.

#### Scenario: The clipboard held an image
- **WHEN** the clipboard held non-text contents before the delivery and the paste succeeds
- **THEN** no restore is attempted and the transcript is left on the clipboard

#### Scenario: The clipboard was empty
- **WHEN** the clipboard held nothing before the delivery and the paste succeeds
- **THEN** no restore is attempted

### Requirement: The transcript is kept out of the operating system's clipboard history
The application SHALL mark what it writes to the clipboard so that the operating system's clipboard
history and cloud clipboard synchronisation do not retain it, and so that clipboard managers that
honour the platform's concealed-content convention do not record it. A transcript is the user's
speech, which this product does not retain anywhere else.

#### Scenario: A transcript is written to the clipboard
- **WHEN** a transcript is placed on the clipboard
- **THEN** it is marked as excluded from clipboard history and from cloud clipboard synchronisation

#### Scenario: The previous contents are restored
- **WHEN** the previous clipboard text is restored after a paste
- **THEN** it is written with the same exclusion, so the restore does not add a duplicate history entry

### Requirement: A clipboard that cannot be written fails the delivery
The application SHALL report a failure, naming what went wrong in terms the user can act on, when
the transcript cannot be placed on the clipboard at all. This is the only outcome in which the
transcript is genuinely lost, and it SHALL be distinguishable from a paste that failed.

#### Scenario: The clipboard cannot be written
- **WHEN** the transcript cannot be placed on the clipboard
- **THEN** the delivery fails with a message describing the failure, and no paste keystroke is sent

#### Scenario: The clipboard is held by another process
- **WHEN** another process is holding the clipboard at the moment of the write
- **THEN** the write is retried for a short period before the delivery is failed

### Requirement: Empty text is never delivered
The application SHALL reject a delivery of text with nothing left once surrounding whitespace is
removed, without touching the clipboard and without sending a keystroke. A transcript that contains
nothing usable is reported as a transcription failure by the capability that produced it, so it
never reaches delivery.

#### Scenario: Whitespace-only text is delivered
- **WHEN** delivery is asked to deliver text that is empty or only whitespace
- **THEN** it is rejected, the clipboard is unchanged and no keystroke is sent

### Requirement: Neither the transcript nor the clipboard's contents are ever logged
The application SHALL NOT write the transcript, or the clipboard contents it read before writing the
transcript, to the log at any level. The transcript is the user's speech and the previous clipboard
contents are as likely as not a password. What may be logged is the transcript's length, the outcome
of the paste, and the reason a restore was skipped.

#### Scenario: A delivery is logged at the most verbose level
- **WHEN** a transcript is delivered with logging at its most verbose level
- **THEN** the log contains neither the transcript text nor the previous clipboard contents, and contains the character count and the outcome
