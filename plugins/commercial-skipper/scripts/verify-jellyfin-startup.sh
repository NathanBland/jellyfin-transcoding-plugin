#!/usr/bin/env bash
set -euo pipefail

archive="${1:?Usage: verify-jellyfin-startup.sh /path/to/commercial-skipper.zip}"
image="${JELLYFIN_TEST_IMAGE:-jellyfin/jellyfin:10.11.11}"
version="$(basename "$archive" .zip)"
version="${version##*_}"
work="$(mktemp -d "$PWD/artifacts/startup-test.XXXXXX")"
container="jellyfin-commercial-skipper-$RANDOM-$$"

cleanup() {
  docker rm --force "$container" >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

mkdir -p "$work/config/plugins/Commercial Skipper_$version" "$work/cache"
unzip -q "$archive" -d "$work/config/plugins/Commercial Skipper_$version"

docker run --detach --name "$container" \
  --volume "$work/config:/config" \
  --volume "$work/cache:/cache" \
  "$image" >/dev/null

for _ in $(seq 1 90); do
  logs="$(docker logs "$container" 2>&1 || true)"
  if grep -q "Error while starting server" <<<"$logs"; then
    echo "$logs"
    exit 1
  fi

  if grep -q "Startup complete" <<<"$logs"; then
    echo "Jellyfin startup completed with Commercial Skipper installed."
    exit 0
  fi

  if [[ "$(docker inspect --format '{{.State.Running}}' "$container" 2>/dev/null || true)" != "true" ]]; then
    echo "$logs"
    echo "Jellyfin stopped before startup completed." >&2
    exit 1
  fi

  sleep 1
done

docker logs "$container"
echo "Timed out waiting for Jellyfin startup." >&2
exit 1
