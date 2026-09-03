## Why

`AutostartReconciler` decides whether the machine agrees with `startWithSystem` by asking
`IAutostartService.IsEnabled()` — one bit, meaning "is something registered". A registration that
exists but names an executable that is no longer this one answers `true`, agrees with an enabled
setting, and is therefore never rewritten. The machine goes on launching the old path at every login
while the General tab truthfully reports that start-at-login is on.

**Reproduced on this machine on 2026-09-03**, during change 12's task 3.2. A developer build had
registered `…/src/Pisum.Whisper.App/bin/Debug/net10.0/Pisum.Whisper.App`; the packaged
`Pisum Whisper.app` was then launched from `artifacts/`, reconciled, and left the launch agent
pointing at the Debug build. Every save since would have done the same.

The path this bites hardest is the one change 12 exists to create: **install the `.msi` or the
`.pkg` over a machine that has ever run this application from a build, and autostart keeps launching
the build.** It is not confined to developers — a macOS user who moves the bundle out of
`/Applications`, or a Windows user who installs a new version to a different location, reaches the
same state.

The capability spec already asks for more than the code does. *Autostart is enabled* requires that
"the registration exists **and names the running executable**"; the reconcile requirement then defines
agreement by existence alone, and the drift it enumerates — a hand-edited settings file, an entry
another tool removed — does not include an entry that is present and wrong. That gap is the bug.

## What Changes

- Replace `IAutostartService.IsEnabled()` with `AutostartRegistration Read()`, returning `Absent`,
  `Stale` or `Current`, where `Current` means *the registration is what `Enable` would write now*.
- `WindowsAutostart.Read` compares the `Run` value with the quoted `Environment.ProcessPath` it would
  write; `MacOsAutostart.Read` compares the plist's whole text with the plist it would write.
- `AutostartReconciler` treats only `Current` as agreement when the setting is on, and only `Absent`
  when it is off. A `Stale` registration is repointed with a single `Enable`, because both native
  implementations overwrite.
- The log line distinguishes the two: `repointed at this executable` rather than `enabled`.
- Tests: three on the reconciler, two on each native implementation.

Reference: **none.** `tauri-plugin-autostart` re-registers unconditionally rather than reconciling,
so it has no equivalent comparison to consult and does not answer this.

## Capabilities

### New Capabilities
_None._

### Modified Capabilities
- `autostart`: a registration that names a different executable is drift like any other, and is
  corrected rather than accepted.

## Impact

Off-sequence, so no number, per `ROADMAP.md`. Code: `IAutostartService`, a new
`AutostartRegistration`, `AutostartReconciler`, `WindowsAutostart`, `MacOsAutostart` and their tests.
No new dependency, no settings-schema change, no UI change.

**It is change 12's finding and it changes change 12's answer.** That change's task 9.4 asks whether
the launch agent points inside `/Applications/Pisum Whisper.app` after an install; without this it
does so only on a machine that had never registered before.

## Non-goals

- **No `launchctl` and no `SMAppService`.** Rewriting the plist is still the whole of the macOS
  mechanism; `launchd` reads the directory at login.
- **No re-registration on every save.** The reconciler still reads before it writes and still writes
  nothing when the registration is already correct — that property is what the extra state protects,
  not something traded away for it.
- **No repair of a registration written by a different application** under a different name or label.
  Only this application's own entry is read and written.
