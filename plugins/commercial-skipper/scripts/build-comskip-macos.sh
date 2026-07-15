#!/usr/bin/env bash
set -euo pipefail

COMSKIP_COMMIT="a140b6ac8bc8f596729e9052819affc779c3b377"
PREFIX="${HOME}/.local"
if [[ "${1:-}" == "--prefix" ]]; then
    [[ -n "${2:-}" ]] || { echo "--prefix requires a path" >&2; exit 2; }
    PREFIX="$2"
elif [[ $# -gt 0 ]]; then
    echo "Usage: $0 [--prefix PATH]" >&2
    exit 2
fi

command -v brew >/dev/null || { echo "Homebrew is required: https://brew.sh" >&2; exit 1; }
missing=()
for package in autoconf automake libtool pkgconf argtable ffmpeg; do
    brew list --versions "$package" >/dev/null 2>&1 || missing+=("$package")
done
if [[ ${#missing[@]} -gt 0 ]]; then
    echo "Install missing dependencies with: brew install ${missing[*]}" >&2
    exit 1
fi

work="$(mktemp -d "${TMPDIR:-/tmp}/commercial-skipper-comskip.XXXXXX")"
trap 'rm -rf "$work"' EXIT
git clone --quiet https://github.com/erikkaashoek/Comskip.git "$work/Comskip"
git -C "$work/Comskip" checkout --quiet "$COMSKIP_COMMIT"
(
    cd "$work/Comskip"
    ./autogen.sh
    ./configure --disable-gui --prefix="$PREFIX"
    make -j "$(sysctl -n hw.ncpu)"
)
mkdir -p "$PREFIX/bin"
install -m 0755 "$work/Comskip/comskip" "$PREFIX/bin/comskip"
"$PREFIX/bin/comskip" --help >/dev/null 2>&1 || true
echo "Installed pinned Comskip $COMSKIP_COMMIT to $PREFIX/bin/comskip"
