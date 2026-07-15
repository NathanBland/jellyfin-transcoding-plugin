#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET:-dotnet}"
CONFIGURATION="${CONFIGURATION:-Release}"
DEFAULT_VERSION="$(sed -n 's/^version: *"\([^"]*\)".*/\1/p' "$ROOT_DIR/build.yaml")"
VERSION="${VERSION:-$DEFAULT_VERSION}"
PROJECT="$ROOT_DIR/src/Jellyfin.Plugin.RecordingTranscoder/Jellyfin.Plugin.RecordingTranscoder.csproj"
PUBLISH_DIR="$ROOT_DIR/artifacts/publish"
STAGING_DIR="$ROOT_DIR/artifacts/package"
ARCHIVE="$ROOT_DIR/artifacts/recording-transcoder_${VERSION}.zip"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid four-part plugin version: $VERSION" >&2; exit 1; }
rm -rf "$PUBLISH_DIR" "$STAGING_DIR" "$ARCHIVE" "$ARCHIVE.sha256" "$ARCHIVE.md5"
mkdir -p "$PUBLISH_DIR" "$STAGING_DIR"
"$DOTNET_BIN" publish "$PROJECT" --configuration "$CONFIGURATION" --no-restore --output "$PUBLISH_DIR" -p:Version="$VERSION" -p:AssemblyVersion="$VERSION" -p:FileVersion="$VERSION"
cp "$PUBLISH_DIR/Jellyfin.Plugin.RecordingTranscoder.dll" "$STAGING_DIR/"
zip -q -j "$ARCHIVE" "$STAGING_DIR/Jellyfin.Plugin.RecordingTranscoder.dll"
ARCHIVE_NAME="$(basename "$ARCHIVE")"
if command -v sha256sum >/dev/null 2>&1; then SHA256="$(sha256sum "$ARCHIVE" | awk '{print $1}')"; else SHA256="$(shasum -a 256 "$ARCHIVE" | awk '{print $1}')"; fi
if command -v md5sum >/dev/null 2>&1; then MD5="$(md5sum "$ARCHIVE" | awk '{print $1}')"; else MD5="$(md5 -q "$ARCHIVE")"; fi
printf '%s  %s\n' "$SHA256" "$ARCHIVE_NAME" > "$ARCHIVE.sha256"
printf '%s  %s\n' "$MD5" "$ARCHIVE_NAME" > "$ARCHIVE.md5"
echo "Created $ARCHIVE"
