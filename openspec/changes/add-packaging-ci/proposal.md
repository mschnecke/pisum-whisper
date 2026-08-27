## Why

Everything so far runs under `dotnet run`. That is precisely the configuration in which the two
hardest platform behaviours are least trustworthy: macOS ties Accessibility grants to a binary's code
signature, so an unsigned or rebuilt-each-time binary re-prompts forever, and Windows toasts need an
AUMID that only an installed Start-menu shortcut provides. **The app is not verified until it is
verified from an installed build.**

## What Changes

- Publish profiles: `win-x64` and `osx-arm64`, self-contained, ReadyToRun.
- Windows: an MSI that installs a Start-menu shortcut **carrying the AUMID**, satisfying the
  requirement recorded in `add-system-integration`.
- macOS: assemble a `Pisum Whisper.app` bundle with `Info.plist` declaring `LSUIElement` (menu-bar
  only), `NSMicrophoneUsageDescription` and `CFBundleIdentifier = net.pisum.whisper`; then codesign
  with the hardened runtime and entitlements, and notarize.
- Establish a **stable signing identity for development**, so Accessibility grants survive rebuilds.
  The reference ships unsigned and strips the quarantine attribute in a postinstall script; that
  papers over the symptom and leaves developers re-granting permission on every build.
- GitHub Actions matrix over `windows-latest` and `macos-latest`, producing both artifacts and
  running the unit test suite — the reference's CI runs no tests at all.
- Document the required prerequisites and permissions in `README.md`.

Reference: `.github/workflows/`, `scripts/create-macos-pkg.sh` and `packages/` in the reference repo.
Note that the repository remote is GitHub, so `gh` is the CLI, not `glab`.

## Capabilities

### New Capabilities
- `packaging`: the application is built, signed and distributed as an installable artifact for Windows x64 and macOS Apple Silicon.

### Modified Capabilities
_None._

## Impact

Depends on every preceding change. Verification here is the real acceptance test for the whole
sequence: install on clean machines and confirm the hotkey, microphone and paste all work from the
installed build, and that the permission prompts appear once and stick.

## Non-goals

- No Chocolatey package and no Homebrew cask. The reference has both; they are a distribution
  decision that can follow once the installers themselves are proven.
- No auto-update mechanism.
- No Linux target, and no win-arm64 or osx-x64.
