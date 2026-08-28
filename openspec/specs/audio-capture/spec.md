# audio-capture Specification

## Purpose

Records microphone audio from the system default input device on demand. It opens no device
picker, applies no resampling or downmixing of its own — the audio backend is asked for 48 kHz
mono directly — and imposes no minimum- or maximum-duration checks; it simply returns everything
captured between start and stop.

## Requirements

### Requirement: Capture uses the system default input device
The system SHALL open the operating system's default audio input device for capture, with no
device-selection option exposed to the user.

#### Scenario: Capture starts
- **WHEN** capture is started
- **THEN** the default input device is opened and begins delivering audio callbacks

#### Scenario: No input device is available
- **WHEN** no input device exists on the system
- **THEN** capture raises an `AudioException` rather than starting silently

### Requirement: Captured audio is requested at 48 kHz mono
The system SHALL request 48 kHz mono 32-bit float samples from the capture device, relying on the
audio backend to convert from the device's native sample rate and channel count rather than
resampling or downmixing itself.

#### Scenario: Device native format differs from the request
- **WHEN** the default device's native format is not 48 kHz mono (for example, 44.1 kHz stereo)
- **THEN** captured samples are still delivered at 48 kHz mono, converted by the audio backend

#### Scenario: Device native format already matches
- **WHEN** the default device is already 48 kHz mono
- **THEN** captured samples are delivered unchanged in rate and channel count

### Requirement: The complete recording is returned when capture stops
The system SHALL return every sample captured since recording started, as a single result, when
capture is stopped. Capture applies no minimum-duration or empty-recording check of its own.

#### Scenario: Capture is stopped after audio was recorded
- **WHEN** capture is stopped after several seconds of recording
- **THEN** the returned samples cover the full duration that was captured

#### Scenario: Capture is stopped immediately
- **WHEN** capture is stopped before any audio callback has fired
- **THEN** an empty sample collection is returned, not an error
