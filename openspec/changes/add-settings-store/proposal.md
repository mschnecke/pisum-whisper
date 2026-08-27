## Why

Every other subsystem reads configuration: the hotkey binding, the Gemini keys and model, the active
preset's system prompt, the log level, the recording mode. Nothing else can be built until there is
one place that owns loading, defaulting, validating and persisting it.

## What Changes

- Add `AppSettings` and its DTOs (`Preset`, `ProviderConfig`, `HotkeyBinding`, `LoggingConfig`) with
  camelCase JSON via a source-generated `JsonSerializerContext`.
- **Every property gets a default.** This is what lets an older or partial file load without error,
  and it is the reason the reference never breaks on upgrade — it is a requirement, not a style choice.
- Add `SettingsStore` reading and writing `~/.pisum-whisper.json`, with an in-memory cache and a
  change notification other services subscribe to.
- Port two load-time repair behaviours exactly from the reference:
  - merge in any **missing built-in presets**, so a new built-in appears for existing users;
  - if `activePresetId` resolves to no preset, fall back to the first built-in **and re-persist**.
- Detect first launch (settings file absent) and expose it, so the welcome flow can hang off it later.
- Add preset operations: upsert by id, delete with built-ins refused, and setting the active preset.
- Write settings atomically, so an interrupted save cannot produce the corrupt file that then blocks
  the next startup. This is the one deliberate deviation from the reference in this change.
- Copy both built-in preset prompts (`de-transcribe`, `en-transcribe`) verbatim. These German prompt
  strings *are* the product's cleanup behaviour — they instruct Gemini to turn dictation into fluent
  written prose and strip filler words.

Settings shape is the reference's minus `transcriptionMode`, `whisperConfig` and `providerType`
(Gemini is now the only provider type). The full shape is enumerated in the *Settings schema*
requirement rather than left as a pointer at the reference, because five later changes read it and
the reference is not in this repository.

Reference: `W:\github-pisum-transcript\src-tauri\src\config\{schema,manager,presets}.rs`.

## Capabilities

### New Capabilities
- `settings-persistence`: application settings load, validate, repair and persist across runs.

### Modified Capabilities
_None._

## Impact

Depends on `bootstrap-solution`. Unblocks `add-file-logging`, `add-global-hotkey`,
`add-gemini-transcription`, and the settings window. Changing the schema after downstream changes
land means touching all of them, so settle the shape here.

## Non-goals

- No settings UI — that is `add-settings-window`.
- No encryption of API keys, and no move to the platform config directory. Both are deliberate
  parity choices with the reference, recorded so they are not mistaken for oversights.
- No reading of the reference's `~/.pisum-transcript.json`. A differently named product gets its own
  file; existing users of the reference start from defaults.
- No reloading of the file when it changes on disk. The store reads it once and is authoritative
  thereafter, so an edit made by hand while the application runs is lost at the next save. A file
  watcher is the upgrade path and needs no API change.
