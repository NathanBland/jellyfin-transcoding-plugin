#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET:-dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
VERSION="${VERSION:-1.0.0.0}"
PROJECT="$ROOT_DIR/src/Jellyfin.Plugin.TranscodingPolicy/Jellyfin.Plugin.TranscodingPolicy.csproj"
PUBLISH_DIR="$ROOT_DIR/artifacts/publish"
STAGING_DIR="$ROOT_DIR/artifacts/Transcoding Policy_${VERSION}"
ARCHIVE="$ROOT_DIR/artifacts/transcoding-policy_${VERSION}.zip"

rm -rf "$PUBLISH_DIR" "$STAGING_DIR" "$ARCHIVE" "$ARCHIVE.sha256"
mkdir -p "$PUBLISH_DIR" "$STAGING_DIR"

"$DOTNET_BIN" publish "$PROJECT" \
    --configuration "$CONFIGURATION" \
    --no-restore \
    --output "$PUBLISH_DIR" \
    -p:Version="$VERSION" \
    -p:AssemblyVersion="$VERSION" \
    -p:FileVersion="$VERSION"

cp "$PUBLISH_DIR/Jellyfin.Plugin.TranscodingPolicy.dll" "$STAGING_DIR/"
cp "$PUBLISH_DIR/0Harmony.dll" "$STAGING_DIR/"

cd "$ROOT_DIR/artifacts"
zip -q -r "$(basename "$ARCHIVE")" "$(basename "$STAGING_DIR")"
shasum -a 256 "$(basename "$ARCHIVE")" > "$(basename "$ARCHIVE").sha256"

echo "Created $ARCHIVE"

