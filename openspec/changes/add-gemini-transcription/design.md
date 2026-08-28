## Context

`add-gemini-transcription` turns the encoded bytes change 4 produces into text. It lives entirely in
`Pisum.Whisper.Core` (namespace `Pisum.Whisper.Core.Transcription`) — HTTP over
`System.Net.Http.IHttpClientFactory` is cross-platform managed code, so nothing lands in `Platform`
and there is no `OperatingSystem.IsWindows()` check anywhere in this change.

The folder name follows the rule the shipped code already established: a capability's folder is
named for its domain concept, not for the reference's module name — `settings-persistence` →
`Settings/`, `file-logging` → `Logging/`, `audio-capture`/`audio-encoding` → `Audio/`,
`global-hotkey` → `Hotkeys/`. So `gemini-transcription` → `Transcription/`, not the reference's `ai/`
and not `openspec/config.yaml`'s aspirational `Ai`.

Both inputs already exist and neither needs changing:

```
  change 4 ──▶ EncodedAudio(byte[] Bytes, string MimeType, AudioFormat ActualFormat)
  change 2 ──▶ SettingsStore.Current.Providers : List<ProviderConfig>
                                                  (Id, ApiKey, Model?, Enabled)
               SettingsStore.Current.Presets / ActivePresetId
```

`SettingsStore` is cache-authoritative (it reads the file once and is the truth thereafter) and
already exposes a `Changed` event that nothing subscribes to yet. That property is what lets this
change drop the reference's pool-rebuild step entirely — see Decisions.

Reference: `W:\github-pisum-transcript\src-tauri\src\ai\{gemini,provider,pool}.rs`, plus
`src-tauri\src\hotkey\manager.rs:455-515` for the call site and `categorize_error`, and
`src-tauri\src\lib.rs:425-460` (`apply_settings`) for the rebuild this change deletes. Three of the
reference's behaviours are deliberately not reproduced; each is called out below and in
`proposal.md`.

## Goals / Non-Goals

**Goals:**

- `ITranscriptionProvider` — the single seam change 8 depends on: encoded audio plus a system prompt
  in, transcript text out, `CancellationToken` honoured.
- `GeminiProvider` — one API key and model, `generateContent` over `IHttpClientFactory`, with the
  reference's retry policy corrected.
- `GeminiProviderPool` — round-robin across enabled entries with walk-all-on-failure, implementing
  `ITranscriptionProvider` itself so change 8 sees one contract.
- `TranscriptionException` + `ErrorCategory` — a failure category fixed at the throw site, so
  change 8's notification titles need no substring matching.
- `IGeminiKeyProbe` — model listing and connection testing for a key the user has just typed, which
  change 10's settings window needs and change 8 does not.

**Non-Goals:**

- No OpenAI provider, no local inference, no streaming or partial transcripts, no cost metering.
- No repo-wide `AppException` base type. `SettingsException` (change 2) and `AudioException`
  (change 4) are not touched, and change 7's output error is not pre-empted.
- No Files API upload path. Oversized recordings are rejected, not chunked or re-uploaded by
  another route.
- No overall time budget across retries and providers. `TranscribeAsync` takes a
  `CancellationToken`; imposing a deadline on it belongs to change 8, which owns the recording state
  machine and its timing constants.
- No retry of the model-listing or connection-test calls — they are user-initiated from a window
  that can simply be clicked again, unlike a dictation the user has already spoken.

## Decisions

**`TranscriptionException` carries an `ErrorCategory`; there is no `AppException` base type.**
`proposal.md` originally called for a repo-wide `AppException` with seven categories. The seven were
over-scoped, because the reference's substring matching lives in exactly one place:

```rust
AppError::Audio(_)           => "Recording Error",       // matched by type already
AppError::Transcription(msg) => { /* all five string tests are here */ }
AppError::Output(_)          => "Output Error",          // matched by type already
```

`AudioException` (change 4) and change 7's output error already do what the `Audio` and `Output` arms
do. What creates the sniffing is that `AppError::Transcription` is one variant covering five distinct
failures. Splitting *that* is the whole fix:

```csharp
// Core/Transcription/ErrorCategory.cs
public enum ErrorCategory { Configuration, Network, Authentication, RateLimit, Transcription }

// Core/Transcription/TranscriptionException.cs — mirrors AudioException's placement and shape
public sealed class TranscriptionException(string message, ErrorCategory category, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ErrorCategory Category { get; } = category;
}
```

Every value is one this change actually raises. Change 8's catch site then becomes the reference's
`categorize_error` structurally, with no string tests and no shipped file touched:

```csharp
static (string Title, string Body) Categorize(Exception exception) => exception switch
{
    TranscriptionException failure => (TitleFor(failure.Category), failure.Message),
    AudioException                 => ("Recording Error",     exception.Message),
    SettingsException              => ("Configuration Error", exception.Message),
    _                              => ("Unexpected Error", "Check the logs for details."),
};
```

**The API key goes in the `x-goog-api-key` header, never the query string.** The reference builds
`…/models/{model}:generateContent?key={api_key}`. Ported as-is, `IHttpClientFactory`'s
`LoggingScopeHttpMessageHandler` logs `Sending HTTP request POST {uri}` at `Information`, and the
default `logLevel` is `info` — so the user's API key would be written to
`~/.pisum-whisper/logs/pisum-whisper.log`, which change 10's "Open Log Folder" button then puts one
click away. That is a direct violation of the project's first logging rule. The header form carries
the same credential with nothing sensitive in the URI, and it also keeps the key out of any
`HttpRequestException` message, which interpolates the request URI. Two consequences to keep:
`GeminiProvider` must never log request headers, and no error message may interpolate anything but
the status code and a truncated *unsuccessful* body.

**The retryable-error predicate is checked only on an unsuccessful response.** The reference tests
the body before the status:

```rust
if Self::is_retryable_error(status, &body) { /* retry */ }   // runs first
if !status.is_success() { return Err(...) }
```

`is_retryable_error` lowercases the entire body and looks for `overloaded`, `too many requests` and
`rate limit`. On a 200 the body *is the transcript*, so a user who dictates "we hit the rate limit
yesterday" has their dictation retried three times and then fails. For a dictation application those
are ordinary words. Here the success path is settled first and the body predicate applies only when
`!response.IsSuccessStatusCode`. This is a deliberate correction of the behavioural spec, in the same
class as change 2's atomic settings write and change 4's lock-free capture buffer.

**3 attempts, not 3 retries.** The reference's `MAX_RETRIES = 3` bounds the loop, so it makes three
attempts separated by two sleeps of `RETRY_DELAY_MS * (attempt + 1)` — 1 s then 2 s. That is what is
reproduced. Transport failures are retried on the same schedule (the reference does this, and a
connection reset is exactly what a retry is for), which is why the spec states the retry trigger as
status-or-transport-or-body rather than the proposal's original "everything else fails immediately".

**`GeminiProviderPool` implements `ITranscriptionProvider`.** The reference keeps `ProviderPool`
outside the `TranscriptionProvider` trait because its error shape differs. Here the aggregated
`"All providers failed: …"` message is just another `TranscriptionException`, so the composite fits
the contract and change 8 gets one seam and one fake instead of two:

```
                 ITranscriptionProvider           ← change 8 depends only on this
                    ▲              ▲
        GeminiProvider          GeminiProviderPool
        (one key + model)       (N GeminiProviders, round robin)
```

`ITranscriptionProvider` is registered to resolve `GeminiProviderPool`; `GeminiProvider` is
constructed by the pool, not by the container.

**The pool reads settings per call and is never rebuilt.** The reference copies settings into a
global `RwLock<ProviderPool>` in `apply_settings` because it has no authoritative in-memory store.
`SettingsStore.Current` is exactly that store, so:

```csharp
var entries = _settings.Current.Providers.Where(entry => entry.Enabled).ToList();
if (entries.Count == 0) throw new TranscriptionException(NoProvidersMessage, ErrorCategory.Configuration);

var start = (int)((uint)Interlocked.Increment(ref _cursor) % (uint)entries.Count);
```

No `Changed` subscription, no rebuild method, no lock, no lifecycle. The only durable state is the
round-robin cursor, an `int` advanced with `Interlocked.Increment`. The snapshot is taken once per
`TranscribeAsync` with `.ToList()`, so a settings save mid-transcription cannot change the entry set
between fallback attempts. The cursor is deliberately *not* reset when the entry set changes: a
modulo over the new count is well-defined, and the only cost of not resetting is which key a single
transcription happens to start from.

`Interlocked.Increment` over an `int` wraps to `int.MinValue` after ~2.1 billion dictations; the cast
to `uint` before the modulo is what keeps the index non-negative rather than throwing.

**`ListModelsAsync` and `TestConnectionAsync` live on `IGeminiKeyProbe`, not on
`ITranscriptionProvider`.** Both are change 10's, and change 10 calls them against *an API key the
user has just typed into a textbox* — not a configured, enabled provider entry. The reference already
recognises this: `list_models` is a static taking an `api_key`, not a trait method. Putting them on
the instance contract would force change 10 to construct a throwaway provider, and would put two
methods change 8 never calls in front of change 8's fake.

```csharp
public interface IGeminiKeyProbe
{
    Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken);
    Task<KeyProbeResult> TestConnectionAsync(string apiKey, string? model, CancellationToken cancellationToken);
}
```

`TestConnectionAsync` returns a result, not `bool`. The reference's `Result<bool, AppError>` never
returns `Ok(false)` — it is `Ok(true)` or an error — so `bool` carries no information the settings
window can display. `KeyProbeResult(bool Succeeded, string Message, ErrorCategory? Category)` gives
the "Test" button both the outcome and the text to show, and lets a failed test render without a
`try`/`catch` around a UI command.

**Wire DTOs get their own source-generated `JsonSerializerContext` with explicit property names.**
The project already serialises settings through a source-generated context so trimming and AOT stay
open (`SettingsJsonContext`); the Gemini DTOs get `GeminiJsonContext` alongside them in
`Core/Transcription/`. No naming policy can be inherited, because the reference's wire shape is
mixed — `system_instruction`, `inline_data` and `mime_type` are snake_case while `generationConfig`
and `maxOutputTokens` are camelCase. Gemini accepts both spellings (proto3 JSON mapping), so every
member carries an explicit `[JsonPropertyName]` in camelCase and the inconsistency is not
reproduced. Serialising with a `null` `system_instruction` omitted is what the connection test
relies on, so the request DTO keeps `JsonIgnoreCondition.WhenWritingNull`.

**Oversized recordings are rejected before the request is built.** Gemini's documentation caps a
`generateContent` request carrying `inline_data` at "20 MB total (including prompts and all files)",
above which the Files API is required. Base64 inflates by 4/3, so the guard is on the raw encoded
length at **14 MiB**, which becomes about 19.6 MB on the wire — under the cap even read as a decimal
20,000,000, with roughly 400 KB left for the system prompt and JSON envelope. 15 MiB, the first
figure this design carried, would not be: it base64-encodes to exactly 20,971,520 bytes, over the cap
before the envelope is counted.

| format | encoded rate | 14 MiB is reached at |
|---|---|---|
| Opus @ 24 kbps | 3 KB/s | ~81 minutes |
| WAV 16-bit mono 48 kHz | 96 KB/s | **~2 min 33 s** |

`MaxRecordingDurationSecs` defaults to 600 s, so the WAV row is reachable in normal use — and change
4's bidirectional fallback can select WAV without the user ever choosing it. The check is one
comparison against `EncodedAudio.Bytes.Length` at the top of `GeminiProvider.TranscribeAsync`,
raising `ErrorCategory.Configuration` with a message naming the size and format. Doing it in the
provider rather than the pool means it fails once rather than N times, but it is also
provider-specific knowledge, which is where it belongs.

**Per-request timeout of 60 s, configured on the named `HttpClient`.** The reference sets none —
reqwest's default is no timeout at all, so a hung upload hangs the dictation for ever. 60 s is
generous for a 15 MB upload plus model latency. The worst case is then 3 attempts x 60 s + 3 s of
backoff per provider, which is why `TranscribeAsync` takes a `CancellationToken`: an overall budget
across providers is change 8's to impose, and change 8 has the state machine to impose it from.

**Registration mirrors `AddAudioPipeline`.**

```csharp
public static IServiceCollection AddGeminiTranscription(this IServiceCollection services)
{
    services.AddHttpClient(GeminiHttpClient.Name, client =>
    {
        client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
        client.Timeout = TimeSpan.FromSeconds(60);
    });

    services.AddSingleton<ITranscriptionProvider, GeminiProviderPool>();
    services.AddSingleton<IGeminiKeyProbe, GeminiKeyProbe>();
    return services;
}
```

`Microsoft.Extensions.Http` is a new pin in `Directory.Packages.props` — nothing in the solution
provides `IHttpClientFactory` today. Called from `Program.cs` next to `AddAudioPipeline()`.

**Testing is against a stub `HttpMessageHandler`, with no network in CI.** `GeminiProvider` takes
`IHttpClientFactory`, so a test supplies a factory over a handler that returns canned responses.
That covers every retry, categorisation, size-guard and extraction scenario in the spec, including
the transcript-mentioning-rate-limits case that motivated the predicate correction. The pool is
tested against `A.Fake<ITranscriptionProvider>()` entries for round-robin, walk-all and aggregation.
No test in this change touches the network or a real API key.

### Rejected alternatives

- **A repo-wide `AppException` base with seven categories** (the proposal's original wording) —
  rejected; two of its seven values duplicate type distinctions that already exist and were never
  sniffed, and it would make `SettingsException` and `AudioException` depend on a type introduced by
  change 5 without either capability's spec recording it.
- **Deferring `ErrorCategory` to change 8** — rejected; the substring matching would survive into the
  hardest change in the roadmap, which is the opposite of what the proposal set out to do.
- **Rebuilding the pool from `SettingsStore.Changed`** — rejected; `Current` is already
  authoritative, so a rebuild only re-derives what a read gives, and it adds a subscription,
  a lock and a lifecycle to a class whose only state is an `int`.
- **`ListModelsAsync`/`TestConnectionAsync` on `ITranscriptionProvider`** — rejected; they answer
  questions about an unsaved key, and would put two methods change 8 never calls in front of change
  8's fake.
- **The reference's `?key=` query parameter** — rejected; it writes the API key to the log file
  through `IHttpClientFactory`'s request-URI logging.
- **A Gemini Files API path for oversized audio** — rejected as scope; a clear rejection with the
  size named is the smaller correct behaviour, and Opus (the default) does not reach the ceiling
  inside any plausible recording length.

## Risks / Trade-offs

- **[Risk]** The 20 MB inline ceiling is taken from Gemini's documentation rather than measured, and
  Google can change it. → **Mitigation:** it is one named constant with the derivation written next
  to it; the spec scenario asserts the guard fires, not the specific number.
- **[Risk]** A WAV fallback silently makes a recording unsendable that would have been fine as Opus,
  and the user only learns at the end of a long dictation. → **Mitigation:** the rejection message
  names both the size and the format, so the cause is legible; change 8 owns whether to warn earlier.
- **[Risk]** Correcting the retry predicate is a deliberate behavioural divergence, and someone
  diffing against `W:\github-pisum-transcript` could read it as a porting mistake. → **Mitigation:**
  recorded in `proposal.md`, here, and as an explicit spec scenario ("A transcript mentions rate
  limits") so a regression fails a test rather than a code review.
- **[Trade-off]** Reading settings per call means a transcription started immediately after a
  settings save uses the new entries. That is the intended behaviour and matches what the reference's
  rebuild achieves, but it makes the entry set non-deterministic across a save, which the
  snapshot-per-call bounds to whole transcriptions.
- **[Trade-off]** Constructing a `GeminiProvider` per attempt allocates, but it holds only a key, a
  model name and a logger; the `HttpClient` and its connection pool come from `IHttpClientFactory`
  and are shared.

## Open Questions

_None outstanding._ The inline-request ceiling was confirmed against
`https://ai.google.dev/gemini-api/docs/audio` during implementation — "Maximum request size is 20 MB
total (including prompts and all files)", with the Files API required above it. That check moved the
raw guard from 15 MiB to 14 MiB (see Decisions); the 15 MiB figure this design first carried would
have exceeded the cap on its own.
