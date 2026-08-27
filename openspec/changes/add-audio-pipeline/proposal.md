## Why

The product's input is the user's voice. Nothing can be transcribed until audio can be captured from
the default microphone and encoded into something Gemini accepts.

## What Changes

- Add `IAudioCapture` with a miniaudio implementation over `SoundFlow.Backends.MiniAudio`.
- **Request 48 kHz mono f32 from the capture device** and let miniaudio convert from whatever the
  device natively runs at. 48 kHz is already a native Opus rate, so this deletes the reference's
  entire sinc-resampling stage (256-tap Blackman-Harris2, run twice for two different targets)
  rather than porting it.
- Accumulate captured audio as `float[]` chunks in a `Channel<float[]>`, downmixed to mono at
  capture. The reference grows one shared `Vec<f32>` under a mutex **locked inside the audio
  callback** — roughly 230 MB for a ten-minute stereo recording, on the realtime thread. This change
  deliberately does not reproduce that.
- Add `OggOpusWriter`: Concentus 2.2.2 (`OPUS_APPLICATION_VOIP`, 24 kbps, 20 ms frames, zero-padded
  tail) plus a hand-rolled Ogg muxer — OpusHead with pre-skip 312, OpusTags, and audio packets whose
  granule positions are 48 kHz-relative.
- Add `WavWriter`: 16-bit PCM RIFF.
- Keep the reference's **bidirectional fallback** — if the preferred format fails to encode, try the
  other before failing the dictation.

Reference: `W:\github-pisum-transcript\src-tauri\src\audio\{recorder,encoder}.rs`
(`encoder.rs:137-210` specifies the Ogg/Opus header layout).

## Capabilities

### New Capabilities
- `audio-capture`: microphone audio is recorded from the system default input device on demand.
- `audio-encoding`: recorded audio is encoded to Ogg/Opus or WAV for upload.

### Modified Capabilities
_None._

## Impact

Depends on `bootstrap-solution`, and on spike S2 having confirmed miniaudio can deliver 48 kHz mono.
Unblocks `add-gemini-transcription`, which needs encoded bytes and a MIME type. If S2 failed, the
capture implementation changes here but `IAudioCapture` does not.

## Non-goals

- No input device picker — the system default only, matching the reference.
- No voice activity detection, noise suppression, gain control or level meter.
- No resampler of our own unless spike S2 proved miniaudio cannot convert.
