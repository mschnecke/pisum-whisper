## ADDED Requirements

### Requirement: Recorded audio is transcribed by Gemini
The system SHALL send encoded audio to Google Gemini's `generateContent` endpoint and return the
transcript text it produces.

#### Scenario: Gemini returns a transcript
- **WHEN** encoded audio is submitted and Gemini responds with a candidate containing text
- **THEN** that text is returned as the transcript

#### Scenario: Gemini returns no candidate
- **WHEN** Gemini responds successfully but with no candidate, no part, or no text
- **THEN** a `TranscriptionException` categorised `Transcription` is raised

#### Scenario: Gemini returns only whitespace
- **WHEN** Gemini responds successfully with text that is empty or whitespace only
- **THEN** a `TranscriptionException` categorised `Transcription` is raised

### Requirement: The request carries the system prompt, the audio and its MIME type
The system SHALL send the caller-supplied system prompt as the request's system instruction, and the
audio as base64-encoded inline data tagged with the MIME type reported by the encoder.

#### Scenario: A request is composed
- **WHEN** a transcription request is built from encoded audio and a system prompt
- **THEN** the request carries that prompt as its system instruction, the audio base64-encoded as
  inline data, and the MIME type the encoder reported for the format actually used

#### Scenario: The encoder fell back to another format
- **WHEN** the encoder reports WAV after failing to produce Opus
- **THEN** the request is tagged with the WAV MIME type rather than the format the user preferred

### Requirement: The API key is never placed in a URL or written to a log
The system SHALL authenticate with Gemini using a request header, and SHALL NOT include the API key
in any request URI, log statement, exception message or user-facing error text.

#### Scenario: A request is authenticated
- **WHEN** any Gemini request is sent
- **THEN** the API key is carried in a request header and the request URI contains no key

#### Scenario: A request fails and is reported
- **WHEN** a request fails and the failure is logged and surfaced as an exception message
- **THEN** neither the log statement nor the message contains the API key

### Requirement: Transient failures are retried and permanent ones are not
The system SHALL retry a request up to 3 attempts in total, sleeping 1 second before the second
attempt and 2 seconds before the third, when the response status is 429 or 503, when the request
failed in transport, or when an unsuccessful response body mentions being overloaded, too many
requests, or a rate limit. Every other unsuccessful response SHALL fail without retrying.

#### Scenario: A 503 is followed by success
- **WHEN** the first attempt returns 503 and the second returns a transcript
- **THEN** the transcript is returned and no third attempt is made

#### Scenario: Retries are exhausted
- **WHEN** all 3 attempts return 429
- **THEN** a `TranscriptionException` categorised `RateLimit` is raised

#### Scenario: A permanent error is returned
- **WHEN** a request returns 400
- **THEN** the failure is raised immediately and no further attempt is made

#### Scenario: A transcript mentions rate limits
- **WHEN** a request succeeds and the transcript text contains the words "rate limit"
- **THEN** the transcript is returned unchanged and the request is not retried

### Requirement: Failures are categorised at the point they are raised
The system SHALL attach an `ErrorCategory` to every `TranscriptionException` it raises, so that
callers can choose how to present a failure without inspecting its message text.

#### Scenario: The key is rejected
- **WHEN** a request returns 401 or 403
- **THEN** a `TranscriptionException` categorised `Authentication` is raised

#### Scenario: The quota is exhausted
- **WHEN** a request returns 429, or a response reports a quota failure
- **THEN** a `TranscriptionException` categorised `RateLimit` is raised

#### Scenario: The request never reached Gemini
- **WHEN** a request fails in transport or times out
- **THEN** a `TranscriptionException` categorised `Network` is raised

#### Scenario: Nothing is configured to transcribe with
- **WHEN** transcription is requested and no enabled provider is configured
- **THEN** a `TranscriptionException` categorised `Configuration` is raised, whose message directs
  the user to add a provider in settings

### Requirement: Transcription is attempted across every enabled provider
The system SHALL distribute requests across the enabled provider entries in round-robin order, and
on failure SHALL try every remaining enabled entry before reporting failure.

#### Scenario: Consecutive transcriptions use different entries
- **WHEN** two transcriptions are requested in succession with more than one enabled entry
- **THEN** they start from different entries

#### Scenario: The first entry tried fails
- **WHEN** the entry a request starts from fails and the next enabled entry succeeds
- **THEN** the transcript from the second entry is returned

#### Scenario: Every entry fails
- **WHEN** all enabled entries fail
- **THEN** a single `TranscriptionException` is raised whose message aggregates each entry's failure

#### Scenario: A disabled entry is configured
- **WHEN** a provider entry is marked disabled
- **THEN** it is never selected and never counted towards the entries tried

#### Scenario: Settings changed since the last transcription
- **WHEN** provider entries are added, removed or disabled and a transcription is then requested
- **THEN** the request uses the entries as they currently stand, with no separate rebuild step

### Requirement: Recordings too large to send inline are rejected before upload
The system SHALL reject encoded audio whose size exceeds Gemini's inline-request ceiling, with a
message naming the size and the format, rather than sending a request that cannot succeed.

#### Scenario: Encoded audio exceeds the ceiling
- **WHEN** encoded audio larger than the inline ceiling is submitted for transcription
- **THEN** a `TranscriptionException` categorised `Configuration` is raised before any request is
  sent, and its message names the encoded size and the audio format

#### Scenario: Encoded audio is within the ceiling
- **WHEN** encoded audio at or below the inline ceiling is submitted
- **THEN** the request is sent normally

### Requirement: An API key can be validated and its models listed
The system SHALL allow an arbitrary API key — including one not yet saved to settings — to be tested
for validity and queried for the models it may use, so the settings window can validate a key and
offer a model list.

#### Scenario: Models are listed for a key
- **WHEN** models are listed for a valid key
- **THEN** the result contains only models that support `generateContent`, each identified without
  the `models/` prefix

#### Scenario: A valid key is tested
- **WHEN** a connection test runs against a valid key and model
- **THEN** the test reports success

#### Scenario: An invalid key is tested
- **WHEN** a connection test runs against a rejected key
- **THEN** the test reports failure with a message suitable for display, categorised
  `Authentication`

### Requirement: Transcription can be cancelled
The system SHALL abandon an in-flight transcription when the caller cancels it, including while
waiting between retry attempts.

#### Scenario: Cancellation during a request
- **WHEN** the caller cancels while a request is in flight or a retry backoff is being awaited
- **THEN** the operation stops without attempting any further provider or attempt

### Requirement: Transcript text is never logged
The system SHALL NOT write transcript text, response bodies from successful requests, or API key
values to the log at any level; it SHALL log outcomes, categories, provider counts and character
counts instead.

#### Scenario: A transcription succeeds
- **WHEN** a transcription completes successfully at any log level
- **THEN** the log records the outcome and the transcript's character count, and contains no part of
  the transcript text
