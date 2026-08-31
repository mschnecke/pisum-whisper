## Why

Captured audio has to become text. Gemini is the transcription backend, and a single API key is a
single point of both failure and quota exhaustion — so the client needs retry and the pool needs
more than one key.

## What Changes

- Add `ITranscriptionProvider` and `GeminiProvider` over `IHttpClientFactory`:
  - `POST /v1beta/models/{model}:generateContent`, the active preset as `system_instruction`, audio
    as base64 `inline_data` with its MIME type, `temperature 0.1`, `maxOutputTokens 8192`.
  - Default model `gemini-2.5-flash-lite`.
  - **The API key travels in the `x-goog-api-key` header, not the reference's `?key=` query
    parameter.** `IHttpClientFactory` logs every request URI at `Information`, and the default
    `logLevel` is `info` — the reference's URL form would write the user's API key into
    `~/.pisum-whisper/logs/`, which change 10's "Open Log Folder" button then puts one click away.
  - Retry on 429/503, on a transport failure, or on a **non-success** body mentioning `overloaded`,
    `too many requests` or `rate limit`: 3 attempts total, sleeping 1 s then 2 s. Every other
    response fails immediately rather than burning seconds on an error that will not resolve.
    The reference tests that body predicate *before* checking the status, so a successful
    transcript containing the words "rate limit" is retried and then fails — for a dictation
    application those are ordinary words, so the predicate is corrected here.
  - Reject audio whose encoded size exceeds Gemini's inline-request ceiling before uploading it,
    naming the cause. WAV is the reachable case: at 48 kHz mono 16-bit it passes the ceiling at
    roughly 2 min 33 s, well inside the 600 s default `MaxRecordingDurationSecs`, and change 4's
    bidirectional fallback can select WAV without the user asking for it.
  - `TranscribeAsync` takes a `CancellationToken`, so change 8 can impose an overall deadline
    across retries and providers.
- Add `GeminiProviderPool`, which **implements `ITranscriptionProvider` itself**: round-robin start
  index across *enabled* provider entries, walking all of them on failure and aggregating into one
  `"All providers failed: …"` error. Change 8 depends on `ITranscriptionProvider` and never learns
  that pools exist. It reads `SettingsStore.Current.Providers` at the start of each call rather
  than being rebuilt when settings change — the store is already cache-authoritative, so a
  rebuild step would only re-derive what a read already gives.
- Add `IGeminiKeyProbe` for the settings window (change 10), which asks about an API key the user
  has just typed rather than one already configured:
  - `ListModelsAsync` — `GET /v1beta/models`, keeping entries that support `generateContent` and
    stripping the `models/` prefix — so the settings window can offer a real dropdown.
  - `TestConnectionAsync` for the settings button.
- Introduce `TranscriptionException` carrying an `ErrorCategory` (`Configuration`, `Network`,
  `Authentication`, `RateLimit`, `Transcription`). The reference decides notification titles by
  substring-matching error text; categorising at the throw site is the same behaviour without the
  string sniffing. The sniffing exists in the reference only because `AppError::Transcription` is
  one variant covering five distinct failures — its `Audio` and `Output` variants are already
  matched by type, and `AudioException` (change 4) and change 7's output error do that here, so
  this change adds no repo-wide base exception and does not touch either.

Reference: `W:\github-pisum-transcript\src-tauri\src\ai\{gemini,provider,pool}.rs`.

## Capabilities

### New Capabilities
- `gemini-transcription`: recorded audio is transcribed to text by Google Gemini, across one or more configured API keys.

### Modified Capabilities
_None._

## Impact

Depends on `add-settings-store` (provider entries, active preset) and `add-audio-pipeline` (encoded
bytes plus MIME type). Unblocks `add-dictation-pipeline`. `ITranscriptionProvider` is also the seam
if a second provider is ever wanted.

## Non-goals

- No OpenAI provider.
- No streaming or partial transcripts — one request, one result.
- No local/offline inference.
- No cost tracking or usage metering.
- No Gemini Files API upload path for recordings that exceed the inline ceiling — they are rejected
  with an explanatory message instead.
- No repo-wide `AppException` base type, and no change to `SettingsException` or `AudioException`.
