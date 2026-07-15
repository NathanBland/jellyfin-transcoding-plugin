# Jellyfin Plugins

This repository contains Jellyfin server plugins maintained by Nathan Bland.
Each plugin is a self-contained package under `plugins/`, while the shared
Jellyfin catalog and GitHub Actions workflows live at the repository root.

## Jellyfin repository

Add this URL under **Dashboard → Plugins → Repositories**:

```text
https://raw.githubusercontent.com/NathanBland/jellyfin-plugins/manifest/manifest.json
```

Then open **Catalog** to install or update a compatible plugin.

## Plugins

| Plugin | Description | Compatibility |
|---|---|---|
| [Transcoding Policy](plugins/transcoding-policy/) | Selectively forces software encoding for matching transcodes, including the macOS VideoToolbox MPEG-2 Live TV workaround. | Jellyfin 10.11.11 |

## Development

The root Makefile delegates to one plugin package. Transcoding Policy is the
current default:

```bash
make ci
make PLUGIN=transcoding-policy ci
```

Each package also has its own README, build metadata, tests, packaging scripts,
and release artifacts.
