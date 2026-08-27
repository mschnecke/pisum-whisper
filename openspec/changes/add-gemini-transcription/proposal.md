## Why

Captured audio has to become text. Gemini is the transcription backend, and a single API key is a
single point of both failure and quota exhaustion — so the client needs retry and the pool needs
more than one key.

## What Changes

- Add `ITranscriptionProvider` and `GeminiProvider` over `IHttpClientFactory`:
  - `POST /v1beta/models/{model}:generateContent`, the active preset as `system_instruction`, audio
    as base64 `inline_data` with its MIME type, `temperature 0.1`, `maxOutputTokens 8192`.
  - Default model `gemini-2.5-flash-lite`.
  - `ListModelsAsync` — `GET /v1beta/models`, keeping entries that support `generateContent` and
    stripping the `models/` prefix — so the settings window can offer a real dropdown.
  - `TestConnectionAsync` for the settings button.
  - Retry 3 times with linear backoff (1 s, 2 s) on 429/503 or a body mentioning `overloaded`,
    `too many requests` or `rate limit`. Everything else fails immediately rather than burning
    seconds on an error that will not resolve.
- Add `GeminiProviderPool`: round-robin start index across *enabled* entries, walking all of them on
  failure and aggregating into one `"All providers failed: …"` error. Rebuilt whenever settings change.
- Introduce typed `AppException` with an `ErrorCategory` (`Audio`, `Configuration`, `Network`,
  `Authentication`, `RateLimit`, `Transcription`, `Output`). The reference decides notification
  titles by substring-matching error text; categorising at the throw site is the same behaviour
  without the string sniffing.

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
