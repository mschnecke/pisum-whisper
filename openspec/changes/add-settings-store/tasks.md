## 1. Schema

- [ ] 1.1 Add `AppSettings` with a default for every property, plus `HotkeyBinding` (modifiers, key) defaulting to Ctrl+Shift+Space, or Cmd+Shift+Space on macOS. Verify: unit test asserts `new AppSettings()` has every documented default.
- [ ] 1.2 Add `Preset` (id, name, systemPrompt, isBuiltin) and `ProviderConfig` (id, apiKey, model, enabled). Verify: unit test round-trips each through JSON unchanged.
- [ ] 1.3 Add `LoggingConfig` (logLevel `info`, logMaxFileSizeMb 1, logRetentionDays 7). Verify: covered by the defaults test in 1.1.
- [ ] 1.4 Add a source-generated `JsonSerializerContext` with camelCase naming. Verify: unit test asserts serialized output contains `startWithSystem` and not `StartWithSystem`.
- [ ] 1.5 Copy both built-in preset prompts verbatim from the reference's `config/presets.rs` into `BuiltinPresets`. Verify: unit test asserts ids `de-transcribe` and `en-transcribe` exist, are marked built-in, and have non-empty prompts.

## 2. Load, repair and save

- [ ] 2.1 Add `SettingsStore` resolving `~/.pisum-whisper.json` and implement `Load`. Verify: unit test against a temp home directory loads a hand-written file.
- [ ] 2.2 Implement first-launch detection: when no file exists, write full defaults and report it. Verify: unit test asserts the file is created, defaults are written, and the first-launch flag is true only on that run.
- [ ] 2.3 Implement partial-file tolerance. Verify: unit test loads a file containing only `{"startWithSystem": false}` and asserts every other property took its default.
- [ ] 2.4 Implement built-in preset merge by id. Verify: two unit tests — a file with no presets gains both built-ins; a file with an edited `de-transcribe` keeps the user's text.
- [ ] 2.5 Implement `activePresetId` repair with write-back and a warning log. Verify: unit test loads a file with a dangling id, asserts the first built-in is active, and asserts the file on disk was corrected.
- [ ] 2.6 Implement `Save`, updating the in-memory cache and raising a change event. Verify: unit test subscribes, saves, and asserts the subscriber received the new values.
- [ ] 2.7 Surface a parse failure with the file path and the underlying error, without overwriting the file. Verify: unit test writes invalid JSON, asserts the exception names the path, and asserts the file is byte-identical afterwards.

## 3. Preset operations

- [ ] 3.1 Implement add, update and delete for presets. Verify: unit tests for each.
- [ ] 3.2 Reject deletion of a built-in preset. Verify: unit test asserts the error and that the list is unchanged.
- [ ] 3.3 Reassign the active preset when the active one is deleted. Verify: unit test asserts the first remaining preset becomes active.

## 4. Wiring

- [ ] 4.1 Register `SettingsStore` as a singleton in the composition root and load settings during startup. Verify: run the app and confirm a log line reports the settings path and whether this was a first launch.
