#!/usr/bin/env bash
# packaging/macos/build-app.sh <version>
#
# Assembles "Pisum Whisper.app" from a published osx-arm64 build. There is no `dotnet` verb for a
# .app, so the layout is built by hand (design D5). Run it, or let `ci.yml` / `release.yml` run it —
# there is deliberately no step that exists only in a workflow file.
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
RID="osx-arm64"

PUBLISH_DIR="$ROOT/artifacts/publish/$RID"
BUNDLE="$ROOT/artifacts/$APP_NAME.app"

rm -rf "$PUBLISH_DIR" "$BUNDLE"

# Self-contained and ReadyToRun; not single-file and not trimmed (design D1). --self-contained is
# what makes "install it and it works" true on a machine with no .NET runtime.
dotnet publish "$ROOT/src/Pisum.Whisper.App" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    -p:PublishReadyToRun=true \
    -p:Version="$VERSION" \
    --output "$PUBLISH_DIR"

# Design D2: native symbol files for third-party C++ that nobody here will ever load into a
# debugger. They are 100 MB of the 228 measured on win-x64; on osx-arm64 the pair is not published
# at all, which is why this is a loop over what may be there rather than two rm calls that must
# both hit. The three managed .pdb files stay — a logged stack trace from an installed build is
# unactionable without line numbers, and they cost 0.2 MB.
for pdb in libSkiaSharp.pdb libHarfBuzzSharp.pdb; do
    rm -f "$PUBLISH_DIR/$pdb"
done

mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"
cp -R "$PUBLISH_DIR/." "$BUNDLE/Contents/MacOS/"

sed "s/__VERSION__/$VERSION/g" "$SCRIPT_DIR/Info.plist.template" > "$BUNDLE/Contents/Info.plist"
plutil -lint "$BUNDLE/Contents/Info.plist" > /dev/null

cp "$ROOT/packaging/icon/AppIcon.icns" "$BUNDLE/Contents/Resources/AppIcon.icns"

# Ad-hoc, with the real identifier (design D6). arm64 Mach-O must carry at least an ad-hoc
# signature to execute at all, so the SDK already applied one — but it reports Identifier=apphost,
# Info.plist=not bound and Sealed Resources=none, so LSUIElement and the microphone purpose string
# sit loose beside the signature rather than under it. Re-signing the assembled bundle fixes all
# three and needs no certificate. "Unsigned" in this project means no Developer ID, never no
# signature.
codesign --force --deep --sign - --identifier "$BUNDLE_ID" "$BUNDLE"
codesign --verify --deep --strict "$BUNDLE"

echo "Created: $BUNDLE"
echo "  Version: $VERSION"
echo "  Size:    $(du -sh "$BUNDLE" | cut -f1)"
