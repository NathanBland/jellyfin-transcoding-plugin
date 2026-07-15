#!/usr/bin/env python3
import argparse
import zipfile
from pathlib import Path

EXPECTED = {"Jellyfin.Plugin.CommercialSkipper.dll"}
parser = argparse.ArgumentParser()
parser.add_argument("--archive", required=True, type=Path)
args = parser.parse_args()
with zipfile.ZipFile(args.archive) as archive:
    entries = archive.infolist()
    names = [entry.filename for entry in entries]
if any(entry.is_dir() for entry in entries) or set(names) != EXPECTED or len(names) != len(set(names)):
    raise ValueError(f"Invalid Commercial Skipper archive layout: {names}")
if any("/" in name or "\\" in name for name in names):
    raise ValueError("Plugin DLL must be at the archive root")
print(f"Validated {args.archive}")
