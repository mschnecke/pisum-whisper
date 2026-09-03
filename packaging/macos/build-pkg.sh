#!/usr/bin/env bash
# packaging/macos/build-pkg.sh <version>
#
# Wraps "Pisum Whisper.app" in the installer a user actually downloads. Builds the bundle first, so
# this is the one command that turns a clean checkout into a macOS installer — the same command a
# person and a workflow run.
#
# Neither pkgbuild nor productbuild is given --sign, deliberately (design D6). The .pkg is the only
# artifact shape that can ship unsigned in one step, because it is the only one with a root script
# to clear quarantine with; see postinstall.
set -euo pipefail

VERSION="${1:-}"
if [ -z "$VERSION" ]; then
    echo "usage: $(basename "$0") <version>    e.g. $(basename "$0") 0.1.0" >&2
    exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

APP_NAME="Pisum Whisper"
BUNDLE_ID="net.pisum.whisper"
BUNDLE="$ROOT/artifacts/$APP_NAME.app"
OUTPUT="$ROOT/artifacts/Pisum.Whisper_${VERSION}_osx-arm64.pkg"

"$SCRIPT_DIR/build-app.sh" "$VERSION"

STAGE="$(mktemp -d)"
# Only `postinstall` may reach the scripts payload. packaging/macos/ also holds the two build
# scripts and the plist template, and --scripts takes a whole directory, so pointing it here — as
# the reference does — would ship them inside the installer.
SCRIPTS="$(mktemp -d)"
trap 'rm -rf "$STAGE" "$SCRIPTS"' EXIT

mkdir -p "$STAGE/Applications"
cp -R "$BUNDLE" "$STAGE/Applications/"
cp "$SCRIPT_DIR/postinstall" "$SCRIPTS/postinstall"
chmod +x "$SCRIPTS/postinstall"

COMPONENT="$(mktemp -d)/component.pkg"
pkgbuild \
    --root "$STAGE" \
    --identifier "$BUNDLE_ID" \
    --version "$VERSION" \
    --install-location "/" \
    --scripts "$SCRIPTS" \
    "$COMPONENT"

productbuild \
    --package "$COMPONENT" \
    --identifier "${BUNDLE_ID}.installer" \
    --version "$VERSION" \
    "$OUTPUT"

rm -rf "$(dirname "$COMPONENT")"

echo "Created: $OUTPUT"
echo "  Version: $VERSION"
echo "  Size:    $(du -h "$OUTPUT" | cut -f1)"
