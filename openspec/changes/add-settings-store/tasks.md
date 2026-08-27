## 1. Schema

- [ ] 1.1 Add `AppSettings` with a default for every property in the *Settings schema* table in `specs/settings-persistence/spec.md` — `startWithSystem`, `showTrayNotifications`, `hotkey`, `audioFormat`, `presets`, `activePresetId`, `providers`, `recordingMode`, `maxRecordingDurationSecs`, `loggingConfig` — plus `HotkeyBinding` (modifiers, key) defaulting to Ctrl+Shift+Space, or Cmd+Shift+Space on macOS. Declare collections with setters, not get-only. Verify: unit test asserts `new AppSettings()` matches the table row for row, including both built-in presets in `presets` and an empty `providers`.
- [ ] 1.2 Add `Preset` (id, name, systemPrompt, isBuiltin) and `ProviderConfig` (id, apiKey, model — nullable, enabled — defaulting true). Mark `Preset.id`, `.name`, `.systemPrompt` and `ProviderConfig.id`, `.apiKey` as required members. Verify: unit test round-trips each through JSON unchanged.
- [ ] 1.3 Add `LoggingConfig` (logLevel `info`, logMaxFileSizeMb 1, logRetentionDays 7). Verify: covered by the defaults test in 1.1.
- [ ] 1.4 Add a source-generated `JsonSerializerContext` with camelCase naming. Verify: unit test asserts serialized output contains `startWithSystem` and not `StartWithSystem`.
- [ ] 1.5 Copy both built-in preset prompts verbatim from the reference's `config/presets.rs` into `BuiltinPresets`. Verify: unit test asserts ids `de-transcribe` and `en-transcribe` exist, are marked built-in, and have non-empty prompts.

- [ ] 1.6 Add `AudioFormat` (`opus`, `wav`) and `RecordingMode` (`holdToRecord`, `toggle`) as enums serialized by a single camelCase `JsonStringEnumConverter`. Verify: unit test asserts the four values serialize to exactly those strings.
- [ ] 1.7 Confirm the three fields dropped from the reference are absent. Verify: unit test serializes defaults and asserts the output contains no `transcriptionMode`, `whisperConfig` or `providerType`.

## 2. Load, repair and save

- [ ] 2.1 Add `SettingsStore` resolving `~/.pisum-whisper.json` and implement `Load`. Verify: unit test against a temp home directory loads a hand-written file.
- [ ] 2.2 Implement first-launch detection: when no file exists, write full defaults and report it. Verify: unit test asserts the file is created, defaults are written, and the first-launch flag is true only on that run.
- [ ] 2.3 Implement partial-file tolerance. Verify: unit test loads a file containing only `{"startWithSystem": false}` and asserts every other property took its default.
- [ ] 2.4 Implement built-in preset merge by id. Verify: two unit tests — a file with no presets gains both built-ins; a file with an edited `de-transcribe` keeps the user's text.
- [ ] 2.5 Implement `activePresetId` repair with write-back and a warning log. Verify: unit test loads a file with a dangling id, asserts the first built-in is active, and asserts the file on disk was corrected.
- [ ] 2.6 Implement `Save`, updating the in-memory cache and raising a change event. Verify: unit test subscribes, saves, and asserts the subscriber received the new values.
- [ ] 2.7 Surface a parse failure with the file path and the underlying error, without overwriting the file. Verify: unit test writes invalid JSON, asserts the exception names the path, and asserts the file is byte-identical afterwards.

- [ ] 2.8 Reject a settings file containing a preset or provider that omits a required identity field. Verify: unit test loads `{"presets":[{"id":"x"}]}` and asserts a parse error naming the file, rather than a preset with a null name.
- [ ] 2.9 Make `Save` atomic — write a temporary file in the same directory and move it over the target. Verify: unit test asserts no partial file is observable, and that an existing file is replaced rather than appended to.

## 3. Preset operations

- [ ] 3.1 Implement preset save as an upsert keyed on id — append an unknown id, and for a known id update only name and system prompt, never `isBuiltin`. Verify: unit tests for append, for update, and one asserting that saving over a built-in leaves it marked built-in and that the edit survives the next load.
- [ ] 3.2 Reject deletion of a built-in preset. Verify: unit test asserts the error and that the list is unchanged.
- [ ] 3.3 Reassign the active preset when the active one is deleted. Verify: unit test asserts the first remaining preset becomes active. Note this fallback is the first **remaining** preset, whereas the load-time repair in 2.5 falls back to the first **built-in** — two deliberately different rules.
- [ ] 3.4 Implement setting the active preset, rejecting an id that matches no preset. Verify: two unit tests — switching to an existing id persists it; switching to an unknown id errors naming the id and leaves the previous preset active.
- [ ] 3.5 Reject deletion of an id that matches no preset. Verify: unit test asserts the error names the id.

## 4. Wiring

- [ ] 4.1 Register `SettingsStore` as a singleton in the composition root and load settings during startup, reading the file once and serving every later read from the cache. Verify: run the app and confirm a log line reports the settings path and whether this was a first launch.
