# Jellyfin Plugins

Independent Jellyfin server plugins maintained by Nathan Bland.

## Install

Add this repository in **Dashboard → Plugins → Repositories**:

```text
Name: Nathan Bland Jellyfin Plugins
URL:  https://raw.githubusercontent.com/NathanBland/jellyfin-plugins/manifest/manifest.json
```

Then open **Catalog**, install a plugin, and restart Jellyfin when prompted.

If a plugin does not appear, confirm that the URL ends in `/manifest/manifest.json`, reload the Catalog page, and restart Jellyfin. Repository download or JSON errors are recorded in the Jellyfin server log. The GitHub project URL itself cannot be used as a Jellyfin repository URL.

## Plugins

| Plugin | Purpose | Requirements and defaults |
|---|---|---|
| [Transcoding Policy](plugins/transcoding-policy/) | Uses software H.264 encoding for matching MPEG-2 Live TV transcodes, working around the macOS VideoToolbox regression without disabling hardware encoding globally. | Jellyfin 10.11.11; enabled with a narrow Live TV rule |
| [Commercial Skipper](plugins/commercial-skipper/) | Runs Comskip on completed recordings and publishes native Jellyfin commercial segments. | Jellyfin 10.11.11 and an external Comskip executable; automatic analysis enabled |
| [Recording Transcoder](plugins/recording-transcoder/) | Replaces completed raw DVR transport streams with validated AV1, HEVC, or H.264 while keeping the original path. | Jellyfin 10.11.11 on any supported OS; follows Jellyfin hardware settings with software fallback; automatic transcoding disabled |

All packages target .NET 9 and are independently installable and versioned.

## Recommended DVR rollout

1. Install and configure [Commercial Skipper](plugins/commercial-skipper/).
2. Verify commercial ranges on several completed recordings.
3. For each Jellyfin user, set **Playback → Media Segment Actions → Commercial → Ask to Skip**.
4. Install [Recording Transcoder](plugins/recording-transcoder/), leaving automatic transcoding disabled.
5. Test a noncritical recording in an isolated library and verify playback, audio, captions, duration, size, and commercial ranges.
6. Expand the selected libraries and enable automatic transcoding only after validation.

When both plugins are installed, a shared lease coordinates processing:

```text
Recording completes
→ Recording Transcoder validates and publishes the selected efficient codec at the original path
→ Commercial Skipper analyzes the final file
→ Jellyfin displays commercial-skip prompts
```

Each plugin also works independently. Library scope follows Jellyfin's configured DVR locations rather than folder names, so paths such as `recordings2` are supported automatically.

## Development

Build one plugin from the repository root:

```bash
make PLUGIN=commercial-skipper ci
make PLUGIN=recording-transcoder ci
make PLUGIN=transcoding-policy ci
```

A .NET 9 SDK, Python 3, `make`, and `zip` are required. Set `DOTNET=/path/to/dotnet` if `dotnet` is not on `PATH`.

Releases use independent four-part tags:

```text
commercial-skipper-v1.0.0.0
recording-transcoder-v1.0.0.0
transcoding-policy-v1.0.0.1
```

The **Release Jellyfin Plugin** workflow publishes the matching ZIP and checksums, then updates that plugin in the shared manifest without removing other plugins or release history.

Licensed under [GPL-3.0-only](LICENSE).
