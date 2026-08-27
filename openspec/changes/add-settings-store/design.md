## Context

Every subsystem in the application reads configuration: the hotkey binding, the Gemini API keys and
model, the active preset's system prompt, the log level, the recording mode and its duration cap.
Nothing else can be built until one component owns loading, defaulting, validating and persisting it.

The reference (`W:\github-pisum-transcript\src-tauri\src\config\`) has been running this schema in
production through several releases without a version field or a migration step. That is worth
understanding rather than copying blindly: it works because *every* field carries a serde default, so
any older file is a valid newer file. That property is the actual design, and it has to be preserved
deliberately.

## Goals / Non-Goals

**Goals:**
- One owner of settings state, with an in-memory cache and change notification.
- Forward and backward compatibility with no migration machinery.
- Self-repair of the two states the reference found worth fixing: missing built-in presets, and a
  dangling `activePresetId`.
- A settled schema shape, since changing it later means touching every downstream change.

**Non-Goals:**
- Any settings UI.
- Encryption of API keys, or moving off the home directory. Both are deliberate parity choices,
  recorded below so they are not mistaken for oversights.

## Decisions

**Defaults on every property, and no schema version field.** A settings file that omits half its
properties must load. This makes adding a property a non-event: old files simply pick up its default.
*Alternative rejected:* a `schemaVersion` field with migration steps. It buys nothing until a
property is renamed or changes meaning, and neither has happened across the reference's lifetime.

**`System.Text.Json` with a source-generated `JsonSerializerContext`.** Avoids reflection-based
serialization, keeps trimming and AOT viable later, and gives compile-time errors on unsupported
shapes.

**camelCase naming to match the reference's on-disk format.** The file is user-editable and users may
already have one; there is no reason to churn the format.

**Settings shape is the reference's minus three fields.** `transcriptionMode` and `whisperConfig` go
with local inference, and `providerType` goes because Gemini is the only provider type. Adding a
single-valued discriminator "in case OpenAI returns" is speculative; `ITranscriptionProvider` is
already the seam that would carry it.

**Repair on load, and re-persist the repair.** When `activePresetId` dangles, fixing it in memory
only means repeating the repair on every launch and leaving a file that still looks broken. Write it
back.

**Built-in presets merge by id, never overwrite.** A user who edits a built-in prompt keeps their
edit; a user missing a newly added built-in gains it. This is why merge is by id rather than by
replacing the built-in set wholesale.

**Copy both built-in preset prompts verbatim.** These long German strings are not boilerplate — they
*are* the product's cleanup behaviour, instructing Gemini to turn dictation into fluent written prose
and strip filler words. Paraphrasing them changes what the product does.

**`SettingsStore` is a singleton with a change event, not a static.** The reference uses a global
`RwLock<AppSettings>`; the equivalent here is a DI singleton, which is testable and disposes cleanly.

**Deliberate parity: API keys stay in plaintext, and the file stays at the home-directory root.**
Both were considered and kept, so that this change is a faithful re-creation rather than a partial
redesign. The upgrade path — DPAPI on Windows, Keychain on macOS, and `%APPDATA%` /
`~/Library/Application Support` — remains open and is not blocked by anything decided here.

## Risks / Trade-offs

- **A corrupt settings file blocks startup.** → The reference surfaces the parse error and refuses to
  continue rather than silently discarding user configuration, which would throw away API keys. Keep
  that behaviour; the message must name the file path so the user can fix or delete it.

- **Repair-on-load can mask a bug.** If `activePresetId` dangles because of a defect elsewhere, silent
  repair hides it. → Log the repair at warning level with the offending id, as the reference does.

- **Plaintext API keys.** → Accepted, deliberately, for parity. Worth revisiting as its own change;
  the store is the only component that would need to change.

- **Settling the schema now means downstream churn if it moves.** → That is precisely why this change
  is early and small: five downstream changes read it, and the cost of getting it wrong grows with
  each one.

## Open Questions

- Should the store debounce writes? The settings UI saves on every change, which for a text field
  could mean a write per keystroke. Deferred to `add-settings-window`, where the input behaviour is
  known; the store's API does not need to change to add it.
