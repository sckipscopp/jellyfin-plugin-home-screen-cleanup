#!/usr/bin/env bash
# Package the published plugin DLL into a Jellyfin plugin zip, reproducibly.
#
# Usage: scripts/package.sh VERSION        (VERSION = X.Y.Z.W, e.g. 1.0.0.0)
# Input:  publish/Jellyfin.Plugin.HomeScreenCleanup.dll (override dir with PUBLISH_DIR), build.yaml,
#         CHANGELOG.md, git history (timestamp of HEAD commit).
# Output: dist/home-screen-cleanup_VERSION.zip (+ .md5, .sha256 in coreutils format),
#         dist/meta.json, dist/notes.md
# Needs:  git, yq (mikefarah v4), jq, zip, md5sum, sha256sum, touch (GNU coreutils).
set -euo pipefail

die() { echo "package.sh: $*" >&2; exit 1; }

VERSION="${1:-}"
[[ -n "$VERSION" ]] || die "usage: package.sh VERSION (e.g. 1.0.0.0)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "VERSION '$VERSION' is not X.Y.Z.W"

for tool in git yq jq zip md5sum sha256sum touch; do
    command -v "$tool" >/dev/null 2>&1 || die "required tool '$tool' not found"
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

DLL="Jellyfin.Plugin.HomeScreenCleanup.dll"
PUBLISH_DIR="${PUBLISH_DIR:-publish}"
DIST_DIR="$ROOT/dist"
ZIP_NAME="home-screen-cleanup_${VERSION}.zip"

[[ -f "$PUBLISH_DIR/$DLL" ]] || die "$PUBLISH_DIR/$DLL not found (run dotnet publish -o $PUBLISH_DIR first)"
[[ -f build.yaml ]] || die "build.yaml not found"

# Deterministic timestamp: committer date of HEAD (the tagged commit in release.yml), in UTC.
TIMESTAMP="$(TZ=UTC git log -1 --format=%cd --date=format-local:%Y-%m-%dT%H:%M:%SZ)"
[[ "$TIMESTAMP" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ ]] || die "bad git timestamp '$TIMESTAMP'"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

cp "$PUBLISH_DIR/$DLL" "$STAGE/$DLL"

# meta.json: build.yaml fields Jellyfin reads + version/timestamp/assemblies. framework and
# artifacts are build-time only and are dropped.
yq -o=json '.' build.yaml \
    | jq --arg version "$VERSION" --arg timestamp "$TIMESTAMP" --arg dll "$DLL" '
        {
          category, changelog, description, guid, name, overview, owner, targetAbi,
          version: $version,
          timestamp: $timestamp,
          assemblies: [$dll]
        }' > "$STAGE/meta.json"

# Every field must be present and non-empty.
jq -e 'to_entries | all(.value != null and .value != "" and .value != [])' "$STAGE/meta.json" >/dev/null \
    || die "meta.json has empty fields: $(cat "$STAGE/meta.json")"

# Reproducible zip: fixed mtimes, no extra attributes (-X), no directory entries (-D), fixed order.
export TZ=UTC
touch -d "$TIMESTAMP" "$STAGE/$DLL" "$STAGE/meta.json"
mkdir -p "$DIST_DIR"
rm -f "$DIST_DIR/$ZIP_NAME" "$DIST_DIR/$ZIP_NAME.md5" "$DIST_DIR/$ZIP_NAME.sha256"
(cd "$STAGE" && zip -X -D -q "$DIST_DIR/$ZIP_NAME" "$DLL" meta.json)

(
    cd "$DIST_DIR"
    md5sum "$ZIP_NAME" > "$ZIP_NAME.md5"
    sha256sum "$ZIP_NAME" > "$ZIP_NAME.sha256"
)
cp "$STAGE/meta.json" "$DIST_DIR/meta.json"

# Release notes: the CHANGELOG.md section for this version ("## X.Y.Z.W ..." up to the next "## ").
NOTES="$(awk -v v="$VERSION" '
    /^## / { if (found) exit; if ($2 == v) { found = 1; next } }
    found { print }
' CHANGELOG.md 2>/dev/null || true)"
if [[ -z "${NOTES//[[:space:]]/}" ]]; then
    NOTES="$(jq -r '.changelog' "$DIST_DIR/meta.json")"
fi
printf '%s\n' "$NOTES" > "$DIST_DIR/notes.md"

echo "Packaged $DIST_DIR/$ZIP_NAME"
cat "$DIST_DIR/$ZIP_NAME.md5" "$DIST_DIR/$ZIP_NAME.sha256"
