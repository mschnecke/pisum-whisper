## Context

`add-audio-pipeline` gives the product its input: capture from the default microphone, encoded into
bytes Gemini will accept. It lives entirely in `Pisum.Whisper.Core` (namespace `Pisum.Whisper.Core.Audio`)
— no `Platform` project involvement, since SoundFlow/miniaudio and Concentus are both cross-platform
managed packages and no P/Invoke of our own is needed.

Two spikes de-risked this change (`openspec/changes/archive/2026-08-27-bootstrap-solution/design.md`):

- **S2** proved miniaudio, asked for 48 kHz mono f32, delivers it accurately on both platforms
  (100.3% Windows, 100.7% macOS) — including a genuine off-native-rate conversion on the Mac's
  44.1/48/88.2/96 kHz microphone. This justifies deleting the reference's `rubato` sinc-resampling
  stage rather than porting it.
- **S4** proved `Concentus.Oggfile`'s `OpusOggWriteStream` already writes a well-formed, decodable
  Ogg/Opus stream on Windows (`OggS`/`OpusHead`/`OpusTags` present, 100.2% duration round-trip over
  201 packets), which supersedes the proposal's original hand-rolled-muxer plan (see `proposal.md`).

Reference: `W:\github-pisum-transcript\src-tauri\src\audio\{recorder,encoder}.rs` for capture/encode
shape, and `src-tauri\src\hotkey\manager.rs:417-446` (`transcribe_cloud`) for the fallback call site —
the encoder module itself has no fallback logic; the caller tries the settings' preferred
`AudioFormat`, and on an `Err` from that encoder, encodes with the other format instead.

## Goals / Non-Goals

**Goals:**
- `IAudioCapture`: start/stop capture from the system default input device, returning the complete
  recording as 48 kHz mono `float[]` samples.
- `IAudioEncoder`: encode those samples to the user's preferred `AudioFormat` (Opus or WAV, from
  `Pisum.Whisper.Core.Settings.AudioFormat`), with a same-request fallback to the other format if the
  preferred one throws, and the MIME type recorded alongside — the caller (`add-gemini-transcription`)
  needs both bytes and MIME type since fallback can silently change which format was actually sent.

**Non-Goals:**
- No input device picker — the system default only, matching the reference.
- No voice activity detection, noise suppression, gain control or level meter.
- No resampler of our own — miniaudio's is proven accurate for the production target (S2).
- No hand-rolled Ogg muxer — `Concentus.Oggfile` already produces a correct one (S4).
- No minimum- or maximum-recording-duration enforcement, and no empty-recording guard. In the
  reference, `MIN_RECORDING_DURATION` (50 ms) and the `samples.is_empty()` →
  `AppError::Audio("No audio recorded")` check both live in `hotkey/manager.rs::process_and_transcribe`,
  not in `audio/recorder.rs` or `audio/encoder.rs` — `AudioRecorderHandle::stop()` just returns
  whatever it captured. The maximum (`AppSettings.MaxRecordingDurationSecs`, default 600s, already
  present in `AppSettings.cs`) is enforced the same way: an external watchdog thread spawned in
  `hotkey/manager.rs::start_recording` races the user's key release and calls the same stop path,
  rather than the recorder stopping itself. `IAudioCapture.StopAsync()` and `IAudioEncoder.Encode()`
  stay equally unaware of all three; that validation and timing belongs to `add-dictation-pipeline`
  (change 8), which owns the hold/release state machine and its timing constants.

## Decisions

**`IAudioCapture` returns a single `Task<float[]>` from `StopAsync`, not a public streaming reader.**
Internally, `MiniAudioCapture`'s `OnAudioProcessed` callback (mirroring `AudioSpike.cs`) writes each
buffer into an unbounded `Channel<float[]>` with `TryWrite` — never a lock held inside the realtime
callback, unlike the reference's `Vec<f32>`-under-mutex. A background loop drains the channel into a
growable buffer. `StopAsync` stops the device, completes the channel, awaits the drain loop, and
concatenates into one `float[]`. The channel is this class's private implementation detail: the
dictation pipeline (change 8) needs the whole recording before it can invoke Gemini, so there's no
consumer for a public per-chunk stream today.

**Mono comes from asking miniaudio for it, not from writing a downmix.** S2 requested `Channels = 1`
directly from a native-stereo device and got an accurate mono stream back — miniaudio's own format
conversion does the downmix. `MiniAudioCapture` requests `AudioFormat { SampleRate = 48_000, Channels
= 1, Format = SampleFormat.F32 }` (SoundFlow's `AudioFormat`/`SampleFormat`, not to be confused with
this project's `Pisum.Whisper.Core.Settings.AudioFormat`) and does no channel math of its own. Unlike
the reference, which threads a dynamic `channels` count through `encode_to_opus`/`encode_to_wav` to
handle a possibly-stereo device, `OggOpusWriter` and `WavWriter` here take no channels parameter at
all — capture is always mono, so there's nothing to thread through.

**A single `AudioException` mirrors `SettingsException`.** `Settings/SettingsException.cs` is one
exception type for the whole capability, with a user-displayable message, matching the reference's
single `AppError::Config` variant. The reference's `AppError::Audio(String)` is the same shape, so
capture and encoding failures both throw one `AudioException`, not a type per failure mode.

**`MiniAudioCapture` has no automated unit test; `OggOpusWriter`/`WavWriter`/`AudioEncoder` do.**
`SettingsStoreTests` can run real file I/O against a temp directory in CI; there's no equivalent for a
microphone. `MiniAudioCapture`'s correctness rests on spike S2's evidence and a manual re-run of the
`audio` spike before release, the same role `combined` already plays for the hotkey/tray work. The
writers and the fallback logic in `AudioEncoder`, by contrast, are pure computation over `float[]`
input and get real MSTest + Shouldly unit tests. `IAudioCapture`/`IAudioEncoder` being interfaces is
what lets `add-dictation-pipeline` (change 8) fake both out later without touching hardware.

**`OggOpusWriter` wraps `Concentus.Oggfile.OpusOggWriteStream`, not a hand-rolled muxer.** Matches the
`OpusSpike.cs` call shape: `OpusCodecFactory.CreateEncoder(sampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP)`
with `Bitrate = 24_000`, then `OpusOggWriteStream.WriteSamples(...)` / `.Finish()` against a
`MemoryStream`. The library owns framing (20 ms-equivalent chunks) and tail handling internally — S4's
100.2% round-trip duration confirms no manual zero-padding code is needed. Emits pre-skip 0, not the
reference's 312 (see `proposal.md`); this is intentional, not a defect.

The constructor's full signature (confirmed by reflecting `Concentus.Oggfile.dll` with the `api` spike:
`dotnet run --project spikes/Pisum.Whisper.Spikes -- api Concentus.Oggfile OpusOggWriteStream`) is
`OpusOggWriteStream(IOpusEncoder encoder, Stream outputStream, OpusTags fileTags, Int32
inputSampleRate, Int32 resamplerQuality, Boolean leaveOpen)`. `resamplerQuality` drives the library's
own internal resampler, which is a no-op here since capture already delivers 48 kHz and the encoder is
constructed for 48 kHz — `OggOpusWriter` passes the same value S4 validated (`5`). `leaveOpen: false`
closes the underlying stream on `Finish()`, so `OggOpusWriter` writes to its own `MemoryStream` rather
than one a caller still needs open afterward.

**`WavWriter` hand-writes a 16-bit PCM RIFF header** — no NuGet package is pinned for WAV (the
reference's `hound` has no equivalent already in `Directory.Packages.props`), and the format is small
enough that a hand-rolled writer is simpler than adding a dependency for it.

**`IAudioEncoder.Encode` mirrors `transcribe_cloud`'s try/fallback exactly**: attempt the preferred
`AudioFormat`'s writer; on any exception, log a warning (format attempted, exception type — never
sample data) and try the other writer; if that also throws, let the exception propagate. Returns an
`EncodedAudio` record (`byte[] Bytes, string MimeType, AudioFormat ActualFormat`) so the caller always
knows which format and MIME type (`audio/ogg` for Opus, `audio/wav` for WAV, matching the reference's
`opus_mime_type()`/`wav_mime_type()`) was actually sent, even after a fallback.

**No platform branching.** SoundFlow/miniaudio and Concentus are both used identically on Windows and
macOS — this change has no `OperatingSystem.IsWindows()` checks and nothing lives in `Platform`.

### Rejected alternatives

- **Hand-rolled Ogg muxer for exact pre-skip 312** — rejected; `Concentus.Oggfile` is already proven
  correct (S4) and the only gain from hand-rolling is an inaudible ~6.5 ms of priming silence.
- **Porting the reference's `rubato` sinc resampler** — rejected; S2 proved miniaudio's own conversion
  is accurate for the 48 kHz mono target on both platforms.
- **Manual stereo→mono downmix** — rejected; requesting mono directly from miniaudio already produces
  it (S2).
- **Public `ChannelReader<float[]>` on `IAudioCapture`** — rejected; no consumer needs per-chunk audio
  before the recording ends, so the channel stays private to `MiniAudioCapture`.

## Risks / Trade-offs

- **[Risk]** Windows capture from a genuinely non-48 kHz-native device was never tested (S2 PARTIAL,
  93.9% sample delivery on a forced 16 kHz request — this machine had no non-48 kHz endpoint at all).
  → **Mitigation:** `IAudioCapture` is an interface specifically so a managed resampler (design.md's
  suggested fallback: `NAudio.Core`'s `WdlResampler`) can be substituted without touching callers;
  re-measure on a real 44.1 kHz Windows input before treating this as closed.
- **[Risk]** Pre-skip 0 vs. the reference's 312 could read as a missed requirement to someone diffing
  against `W:\github-pisum-transcript`. → **Mitigation:** recorded explicitly in `proposal.md` and here
  as a deliberate, spike-verified deviation, not an oversight.

## Open Questions

_None outstanding._ The `OpusOggWriteStream` constructor parameters were resolved by reflecting
`Concentus.Oggfile.dll` (see Decisions above) rather than left as an assumption.
