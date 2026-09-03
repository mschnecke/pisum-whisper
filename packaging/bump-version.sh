#!/usr/bin/env bash
# packaging/bump-version.sh <major|minor|patch|X.Y.Z[-suffix]>
#
# Writes the next version into Directory.Build.props and prints it. That file is the only place in
# the tree carrying the current version, so it is both what this reads and what it rewrites; every
# artifact of a release still takes its version from the tag (design D8), and this is what decides
# what that tag will say.
#
# The same command a person and the release workflow run: nothing about a release exists only inside
# .github/workflows/. It touches git not at all — committing, tagging and pushing are the caller's,
# so running this by hand is a one-file edit that `git diff` shows and `git checkout` undoes.
#
# The new version goes to stdout alone. Everything else goes to stderr, so `VERSION=$(bump-version.sh
# patch)` is the whole of the calling contract.
set -euo pipefail

BUMP="${1:-}"
if [ -z "$BUMP" ]; then
    echo "usage: $(basename "$0") <major|minor|patch|X.Y.Z[-suffix]>" >&2
    exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROPS="$(cd "$SCRIPT_DIR/.." && pwd)/Directory.Build.props"

CURRENT="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROPS")"
if [ -z "$CURRENT" ] || [ "$(printf '%s\n' "$CURRENT" | wc -l)" -ne 1 ]; then
    echo "error: expected exactly one <Version> element in $PROPS, found: ${CURRENT:-none}" >&2
    exit 1
fi

# A pre-release suffix is dropped rather than carried through, and the core is what a keyword bump
# resolves to: X.Y.Z-rc.1 is a rehearsal for X.Y.Z, so X.Y.Z is the next version to release by any
# reading of it, and bumping past it would skip a number that was never published. Cutting a further
# pre-release from a pre-release means passing the exact version instead.
CORE="${CURRENT%%-*}"
PRERELEASE="${CURRENT#"$CORE"}"

if ! printf '%s' "$CORE" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    echo "error: '$CURRENT' in $PROPS is not a version this can bump." >&2
    exit 1
fi
IFS=. read -r MAJOR MINOR PATCH <<<"$CORE"

EXACT=
case "$BUMP" in
    major) NEW="$((MAJOR + 1)).0.0" ;;
    minor) NEW="$MAJOR.$((MINOR + 1)).0" ;;
    patch) NEW="$MAJOR.$MINOR.$((PATCH + 1))" ;;
    *)
        # Anything else is an exact version, and it is validated here rather than at the tag: a
        # version Windows Installer or NuGet refuses is a release that fails after both installers
        # have already been built.
        if ! printf '%s' "$BUMP" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$'; then
            echo "error: '$BUMP' is neither major, minor, patch nor a version of the form X.Y.Z[-suffix]." >&2
            exit 2
        fi
        NEW="$BUMP"
        EXACT=1
        ;;
esac

if [ -z "$EXACT" ] && [ -n "$PRERELEASE" ]; then
    NEW="$CORE"
fi

if [ "$NEW" = "$CURRENT" ]; then
    echo "error: $PROPS already says $NEW." >&2
    exit 1
fi

# A temp file and a move rather than `sed -i`, whose in-place flag takes an argument on BSD sed and
# not on GNU's, and this runs on both.
TMP="$(mktemp)"
sed "s:<Version>$CURRENT</Version>:<Version>$NEW</Version>:" "$PROPS" > "$TMP"
mv "$TMP" "$PROPS"

echo "$CURRENT -> $NEW" >&2
echo "$NEW"
