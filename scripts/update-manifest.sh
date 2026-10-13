#!/usr/bin/env bash
# Add a released version to the Jellyfin plugin repository manifest.
#
# Usage: scripts/update-manifest.sh META_JSON MD5 URL MANIFEST
#   META_JSON  dist/meta.json written by package.sh
#   MD5        the zip's MD5 as 32 hex chars, or a path to the coreutils .md5 file
#   URL        public download URL of the zip (GitHub release asset)
#   MANIFEST   manifest.json to update in place
#
# New plugin guid -> append a plugin entry. Known guid -> prepend the version (newest first) and
# refresh the plugin-level text from meta.json. Exits 1 if the version is already listed, so a
# re-run can never rewrite the checksum of a published version.
set -euo pipefail

die() { echo "update-manifest.sh: $*" >&2; exit 1; }

[[ $# -eq 4 ]] || die "usage: update-manifest.sh META_JSON MD5 URL MANIFEST"
META="$1"; MD5_ARG="$2"; URL="$3"; MANIFEST="$4"

command -v jq >/dev/null 2>&1 || die "jq not found"
[[ -f "$META" ]] || die "meta file '$META' not found"
[[ -f "$MANIFEST" ]] || die "manifest '$MANIFEST' not found"
[[ "$URL" =~ ^https://[^[:space:]]+\.zip$ ]] || die "URL must be an https .zip URL"

if [[ -f "$MD5_ARG" ]]; then
    MD5="$(awk 'NR == 1 { print $1 }' "$MD5_ARG")"
else
    MD5="$MD5_ARG"
fi
MD5="$(printf '%s' "$MD5" | tr '[:upper:]' '[:lower:]')"
[[ "$MD5" =~ ^[0-9a-f]{32}$ ]] || die "MD5 '$MD5' is not 32 hex characters"

jq -e 'type == "array"' "$MANIFEST" >/dev/null || die "manifest is not a JSON array"
jq -e '.guid and .name and .version and .targetAbi and .timestamp' "$META" >/dev/null \
    || die "meta.json is missing guid/name/version/targetAbi/timestamp"

GUID="$(jq -r '.guid' "$META")"
VERSION="$(jq -r '.version' "$META")"

if jq -e --arg g "$GUID" --arg v "$VERSION" \
    'any(.[]; (.guid | ascii_downcase) == ($g | ascii_downcase) and any(.versions[]?; .version == $v))' \
    "$MANIFEST" >/dev/null; then
    die "version $VERSION of $GUID is already in $MANIFEST; refusing to rewrite it"
fi

TMP="$(mktemp "${MANIFEST}.XXXXXX")"
trap 'rm -f "$TMP"' EXIT

jq --slurpfile meta "$META" --arg md5 "$MD5" --arg url "$URL" '
    ($meta[0]) as $m
    | {
        version: $m.version,
        changelog: $m.changelog,
        targetAbi: $m.targetAbi,
        sourceUrl: $url,
        checksum: $md5,
        timestamp: $m.timestamp
      } as $ver
    | if any(.[]; (.guid | ascii_downcase) == ($m.guid | ascii_downcase)) then
        map(if (.guid | ascii_downcase) == ($m.guid | ascii_downcase) then
              .name = $m.name
              | .description = $m.description
              | .overview = $m.overview
              | .owner = $m.owner
              | .category = $m.category
              | .versions = ([$ver] + (.versions // []))
            else . end)
      else
        . + [{
          guid: $m.guid,
          name: $m.name,
          description: $m.description,
          overview: $m.overview,
          owner: $m.owner,
          category: $m.category,
          versions: [$ver]
        }]
      end
' "$MANIFEST" > "$TMP"

jq -e 'type == "array"' "$TMP" >/dev/null || die "generated manifest is invalid"
mv "$TMP" "$MANIFEST"
trap - EXIT

echo "Added $VERSION ($MD5) to $MANIFEST"
