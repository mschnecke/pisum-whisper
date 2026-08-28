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

**Non-ASCII is written as itself, not escaped.** `System.Text.Json`'s default encoder escapes every
character outside ASCII, which writes the German built-in prompts as a wall of `ü` and makes the
file unreadable exactly where a user is most likely to want to edit it. Serializing with
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` restores the reference's output. What that encoder
relaxes is escaping intended for HTML contexts — `<`, `>`, `&`, `'`, `+` — and a settings file is not
one; nothing reads it into a page. Its name warrants the check, not avoidance. This is the mechanism
that makes the hand-editability claim above true rather than nominal, so the two decisions stand or
fall together. It cannot be expressed on `JsonSourceGenerationOptions`, which has no `Encoder`, so
the store serializes through a context built over options that carry it rather than through
`Default`.

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

**The store is cache-authoritative.** Settings are read once at startup; mutations change the
in-memory copy, write the file, then raise the change event. *Alternative rejected:* the reference's
model, where every mutation re-reads the file first. That preserves a hand-edit made while the app is
running, but it re-runs the built-in merge and the `activePresetId` repair on every write, which
means a mutation can trigger a nested save. The cost of the choice is real and is recorded under
Risks: an external edit made while the application is running is overwritten by the next save.

**Writes are atomic — a deliberate deviation from the reference.** The reference writes over the file
in place, so an interruption mid-write truncates it, and the next launch refuses to start on a file
the application itself corrupted. Writing to a temporary file and moving it over the target removes
that failure mode. This is the only behaviour in this change that intentionally differs from the
reference, and it is a strict improvement rather than a redesign.

**List elements keep required identity fields.** The reference gives `Preset.id`, `.name`,
`.system_prompt` and `ProviderConfig.id`, `.api_key` no serde default, so a malformed element fails
the whole parse. The naive port does not preserve that: `System.Text.Json` leaves an absent
non-nullable `string` as `null` without complaint, which defers the failure to a null reference
somewhere downstream. Marking those members required restores the reference's behaviour and keeps the
"every property has a default" rule honest by scoping it to the settings root and its configuration
objects.

**`new AppSettings()` carries the built-in presets.** The reference has *two* different defaults for
`presets`: the struct's `Default` yields both built-ins, while the field's `#[serde(default)]` yields
an empty list. A C# property initialiser gives only one, and the end state is identical either way
because the merge runs unconditionally — so take the struct's, which is what first launch writes.
This also reframes the merge: it is not only how a newly added built-in reaches existing users, it is
what makes any partial file work at all. It must stay on the load path rather than becoming a
first-launch step.

**The file is renamed, and the reference's file is not read.** The reference stores
`~/.pisum-transcript.json`; this application uses `~/.pisum-whisper.json`. No attempt is made to
adopt the old file — a differently named product gets its own settings. Note the consequence for the
camelCase decision above: since no existing file will be found at the new path, camelCase is kept for
hand-editability and reference parity, not for compatibility with files users already have.

**One camelCase enum converter reproduces both of the reference's conventions.** The reference is
inconsistent — `AudioFormat` is `rename_all = "lowercase"` while `RecordingMode` is `camelCase` — but
every `AudioFormat` variant is a single word, so both render identically under a single camelCase
`JsonStringEnumConverter`. No per-enum configuration is needed.

**Collections are declared with setters.** `System.Text.Json` replaces a settable collection property
on deserialization but *appends* to a get-only one, which would duplicate the built-in presets on
every load. Worth stating because the bug it prevents is silent and grows the file on each run.

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

- **A cache-authoritative store discards external edits.** The settings file is advertised as
  user-editable, but a hand-edit made while the application is running is lost at the next save. →
  Accepted for now. The upgrade path is a file watcher that reloads and re-notifies, which can be
  added without changing the store's API; it needs debouncing and must ignore the store's own writes
  to avoid a reload loop.

- **Settling the schema now means downstream churn if it moves.** → That is precisely why this change
  is early and small: five downstream changes read it, and the cost of getting it wrong grows with
  each one.

## Open Questions

- Should the store debounce writes? The settings UI saves on every change, which for a text field
  could mean a write per keystroke. Deferred to `add-settings-window`, where the input behaviour is
  known; the store's API does not need to change to add it.
