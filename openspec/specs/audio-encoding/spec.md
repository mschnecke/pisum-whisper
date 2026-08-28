# audio-encoding Specification

## Purpose

Encodes captured samples into the format Gemini will be sent — Ogg/Opus or WAV, chosen by the
user's settings — and falls back to the other format if the preferred encoder fails, so a
dictation is never lost to a single encoder error.

## Requirements

### Requirement: Audio is encoded to the user's preferred format
The system SHALL encode captured samples to the format configured in settings (`AudioFormat.Opus` or
`AudioFormat.Wav`), returning the encoded bytes together with the correct MIME type for that format.

#### Scenario: Preferred format is Opus
- **WHEN** the configured `AudioFormat` is `Opus` and encoding succeeds
- **THEN** the result contains an Ogg/Opus byte stream with MIME type `audio/ogg`

#### Scenario: Preferred format is WAV
- **WHEN** the configured `AudioFormat` is `Wav` and encoding succeeds
- **THEN** the result contains a WAV byte stream with MIME type `audio/wav`

### Requirement: Encoding falls back to the other format on failure
The system SHALL retry encoding with the non-preferred format if the preferred format's encoder
raises an exception, and report which format was actually used so the caller sends a matching MIME
type.

#### Scenario: Preferred format fails, fallback succeeds
- **WHEN** the preferred format's encoder throws and the other format's encoder succeeds
- **THEN** the result contains the fallback format's bytes and MIME type, and reports that format as
  the one actually used

#### Scenario: Both formats fail
- **WHEN** both the preferred and fallback encoders throw
- **THEN** an `AudioException` is raised

### Requirement: Opus output is decodable and duration-accurate
The system SHALL produce an Ogg/Opus stream that a standard Opus/Ogg reader can decode back to the
same duration as the source audio.

#### Scenario: Opus-encoded audio is decoded back
- **WHEN** Opus-encoded output is decoded with an Ogg/Opus reader
- **THEN** the decoded audio duration matches the source sample duration

### Requirement: WAV output is uncompressed 16-bit PCM
The system SHALL produce a WAV file with a valid RIFF header describing 16-bit PCM samples at the
source sample rate.

#### Scenario: WAV output is inspected
- **WHEN** WAV-encoded output's header is read
- **THEN** it describes 16-bit PCM samples at the sample rate the audio was captured at
