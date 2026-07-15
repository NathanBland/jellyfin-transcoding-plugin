#!/usr/bin/env python3
"""Add or replace one plugin release in a Jellyfin repository manifest."""

from __future__ import annotations

import argparse
import json
import os
import re
import tempfile
from pathlib import Path
from typing import Any


VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
MD5_PATTERN = re.compile(r"^[0-9a-fA-F]{32}$")
PACKAGE_FIELDS = (
    "guid",
    "name",
    "description",
    "overview",
    "owner",
    "category",
    "imageUrl",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--template", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source-url", required=True)
    parser.add_argument("--checksum", required=True)
    parser.add_argument("--timestamp", required=True)
    parser.add_argument("--changelog", required=True)
    return parser.parse_args()


def version_key(version: str) -> tuple[int, int, int, int]:
    if not VERSION_PATTERN.fullmatch(version):
        raise ValueError(f"Version must have four numeric parts: {version}")
    return tuple(int(part) for part in version.split("."))  # type: ignore[return-value]


def update_manifest(
    manifest: list[dict[str, Any]],
    metadata: dict[str, Any],
    *,
    version: str,
    source_url: str,
    checksum: str,
    timestamp: str,
    changelog: str,
) -> list[dict[str, Any]]:
    version_key(version)
    if not source_url.lower().endswith(".zip"):
        raise ValueError("Jellyfin plugin sourceUrl must end in .zip")
    if not MD5_PATTERN.fullmatch(checksum):
        raise ValueError("Jellyfin plugin checksum must be a 32-character MD5")

    guid = metadata["guid"]
    target_abi = metadata["targetAbi"]
    package = next((item for item in manifest if item.get("guid") == guid), None)
    if package is None:
        package = {field: metadata[field] for field in PACKAGE_FIELDS if field in metadata}
        package["versions"] = []
        manifest.append(package)
    else:
        for field in PACKAGE_FIELDS:
            if field in metadata:
                package[field] = metadata[field]

    versions = [
        item for item in package.get("versions", []) if item.get("version") != version
    ]
    versions.append(
        {
            "version": version,
            "changelog": changelog,
            "targetAbi": target_abi,
            "sourceUrl": source_url,
            "checksum": checksum.lower(),
            "timestamp": timestamp,
        }
    )
    package["versions"] = sorted(
        versions,
        key=lambda item: version_key(item["version"]),
        reverse=True,
    )
    return manifest


def load_json(path: Path, default: Any) -> Any:
    if not path.exists():
        return default
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def write_json_atomic(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        dir=path.parent,
        prefix=f".{path.name}.",
        delete=False,
    ) as handle:
        json.dump(value, handle, indent=2)
        handle.write("\n")
        temporary_path = Path(handle.name)
    os.replace(temporary_path, path)


def main() -> None:
    args = parse_args()
    manifest = load_json(args.manifest, [])
    metadata = load_json(args.template, None)
    if not isinstance(manifest, list):
        raise ValueError("Manifest root must be a JSON array")
    if not isinstance(metadata, dict):
        raise ValueError("Plugin template must be a JSON object")

    result = update_manifest(
        manifest,
        metadata,
        version=args.version,
        source_url=args.source_url,
        checksum=args.checksum,
        timestamp=args.timestamp,
        changelog=args.changelog,
    )
    write_json_atomic(args.manifest, result)


if __name__ == "__main__":
    main()
