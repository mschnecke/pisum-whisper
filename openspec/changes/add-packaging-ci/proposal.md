# Package and release the application

## Why

Everything so far runs under `dotnet run` — the configuration in which the platform behaviours are
least trustworthy, since macOS ties the Accessibility grant to a binary's code identity and nothing
here has launched from an install. **The app is not verified until it is
verified from an installed build.**

## What Changes

- Publish `win-x64` and `osx-arm64`: self-contained, ReadyToRun, not trimmed, not single-file.
- Windows: a WiX v6 MSI with a Start-menu shortcut carrying an AUMID.
- macOS: `Pisum Whisper.app` — `Info.plist` declaring `LSUIElement`,
  `NSMicrophoneUsageDescription` and `CFBundleIdentifier = net.pisum.whisper` — inside a `.pkg`
  whose postinstall clears quarantine.
- **Ship unsigned on both platforms, deliberately.** *Reversing the first draft*, which called the
  reference's quarantine strip "papering over the symptom" and set out to establish a signing
  identity. The reference has shipped eighteen releases unsigned through a public Homebrew cask, a
  Developer ID is a purchase, and an unsigned `.pkg` is the only shape keeping installation to one
  step. The price — the Accessibility grant re-prompting after every update, an ad-hoc signature's
  identity being its cdhash — is in `README.md` and the cask caveats, not hidden.
- An application icon; `App/Assets/` holds tray glyphs only.
- GitHub Actions: `ci.yml` builds and tests on `windows-latest` and `macos-latest`; `release.yml`
  publishes both installers on a `v*` tag. The reference's CI runs no tests.
- A Homebrew cask in `mschnecke/homebrew-pisum-whisper`; a Chocolatey package on MyGet.
- `README.md`: prerequisites, permissions, both unsigned-install detours.

Reference: `.github/workflows/`, `scripts/create-macos-pkg.sh`, `scripts/postinstall`, `packages/`,
and its tap's cask.

## Capabilities

### New Capabilities
- `packaging`: the application is built, released by CI, and distributed as an installable artifact
  for Windows x64 and macOS Apple Silicon.

### Modified Capabilities
_None._

## Impact

Depends on every preceding change; verification here is the sequence's acceptance test.
**Task group 5 also depended on `ready-the-suite-for-ci`**, now archived on this branch: it waits
on that merge, not on that change.

**One claim is withdrawn.** The first draft said the shortcut's AUMID satisfies "the requirement
recorded in `add-system-integration`". Change 11 records the opposite: it drew its own notification
window to place *no* requirement here. The AUMID is set anyway — reviving `spikes -- notify`'s
three unanswered questions — but satisfies nothing.

## Non-goals

- No code signing, no notarization.
- No auto-update mechanism.
- No Linux, win-arm64 or osx-x64 target.
- No version-bump automation; tags are written by hand.
