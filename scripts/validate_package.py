#!/usr/bin/env python3
"""Validate the archive layout expected by Jellyfin's plugin installer."""

from __future__ import annotations

import argparse
import zipfile
from pathlib import Path


EXPECTED_FILES = {
    "0Harmony.dll",
    "Jellyfin.Plugin.TranscodingPolicy.dll",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive", type=Path, required=True)
    return parser.parse_args()


def validate_archive(path: Path) -> None:
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]

    if any(entry.is_dir() for entry in entries):
        raise ValueError("Plugin archive must not contain directories")
    if len(names) != len(set(names)):
        raise ValueError("Plugin archive contains duplicate entries")
    if set(names) != EXPECTED_FILES:
        raise ValueError(
            f"Expected exactly {sorted(EXPECTED_FILES)}, found {sorted(names)}"
        )
    if any("/" in name or "\\" in name for name in names):
        raise ValueError("Plugin DLLs must be at the root of the archive")


def main() -> None:
    args = parse_args()
    validate_archive(args.archive)
    print(f"Validated flat Jellyfin plugin archive: {args.archive}")


if __name__ == "__main__":
    main()
