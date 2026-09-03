# Package and release the application

## Why

Everything so far runs under `dotnet run` — the configuration in which the platform behaviours are
least trustworthy, since macOS ties the Accessibility grant to a binary's code identity and neither
platform has launched this application from an install. **The app is not verified until it is
verified from an installed build.**

## What Changes

- Publish `win-x64` and `osx-arm64`: self-contained, ReadyToRun, not trimmed, not single-file.
- Windows: a WiX v6 MSI with a Start-menu shortcut carrying an AUMID.
- macOS: assemble `Pisum Whisper.app` — `Info.plist` declaring `LSUIElement`,
  `NSMicrophoneUsageDescription` and `CFBundleIdentifier = net.pisum.whisper` — inside a `.pkg`
  whose postinstall clears the quarantine attribute.
- **Ship unsigned on both platforms, deliberately.** *This reverses the first draft*, which called
  the reference's quarantine strip "papering over the symptom" and set out to establish a signing
  identity. The reference has shipped eighteen releases unsigned through a public Homebrew cask, a
  Developer ID is a purchase, and an unsigned `.pkg` is the only shape keeping the install to one
  step. The price — the Accessibility grant re-prompting after every update, an ad-hoc signature's
  identity being its cdhash — is told to the user in `README.md` and the cask caveats, not hidden.
- An application icon: `App/Assets/` holds tray glyphs only.
- GitHub Actions: `ci.yml` builds and tests on `windows-latest` and `macos-latest`; `release.yml`
  publishes both installers on a `v*` tag. The reference's CI runs no tests at all.
- A Homebrew cask in `mschnecke/homebrew-pisum-whisper`; a Chocolatey package pushed to MyGet.
- `README.md`: prerequisites, permissions, and both unsigned-install detours.

Reference: `.github/workflows/`, `scripts/create-macos-pkg.sh`, `scripts/postinstall`, `packages/`,
and its tap's `casks/pisum-transcript.rb`.

## Capabilities

### New Capabilities
- `packaging`: the application is built, released by CI, and distributed as an installable artifact
  for Windows x64 and macOS Apple Silicon.

### Modified Capabilities
_None._

## Impact

Depends on every preceding change; verification here is the whole sequence's acceptance test.

**One claim is withdrawn.** The first draft said the shortcut's AUMID satisfies "the requirement
recorded in `add-system-integration`". Change 11 records the opposite — it drew its own notification
window so as to place *no* requirement here. The AUMID is set anyway, reviving `spikes -- notify`'s
three unanswered questions, but it satisfies nothing.

## Non-goals

- No code signing and no notarization.
- No auto-update mechanism.
- No Linux target, no win-arm64, no osx-x64.
- No version-bump automation; the tag is written by hand.
