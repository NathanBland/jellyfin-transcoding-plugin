# Commercial Skipper

Commercial Skipper detects advertisements in completed Jellyfin DVR recordings with [Comskip](https://github.com/erikkaashoek/Comskip) and publishes native Jellyfin `Commercial` media segments. It never modifies, renames, remuxes, or deletes a recording.

Requires Jellyfin 10.11.11 / .NET 9 and an external Comskip executable.

## Install

1. Install Comskip on the Jellyfin server.
2. Add the repository URL from the [root README](../../README.md) to Jellyfin.
3. Install **Commercial Skipper** from **Dashboard → Plugins → Catalog**.
4. Restart Jellyfin.
5. Open **Dashboard → Plugins → My Plugins → Commercial Skipper**.
6. Set the Comskip executable path, save, and select **Test Comskip**.

### Build Comskip on Apple Silicon macOS

The repository includes a non-root helper pinned to Comskip commit `a140b6ac8bc8f596729e9052819affc779c3b377`:

```bash
brew install autoconf automake libtool pkgconf argtable ffmpeg
cd plugins/commercial-skipper
./scripts/build-comskip-macos.sh
```

It installs `comskip` to `~/.local/bin/comskip`. To choose another location:

```bash
./scripts/build-comskip-macos.sh --prefix /path/to/prefix
```

Enter the resulting executable path on the plugin page. Comskip is not bundled in the Jellyfin plugin ZIP.

## Configure skip prompts

Commercial Skipper creates the segment data; Jellyfin controls how each user sees it. For every user who should receive prompts, set:

**Playback → Media Segment Actions → Commercial → Ask to Skip**

If Intro Skipper is installed, disable its commercial-chapter scan to avoid duplicate ranges. Commercial Skipper does not depend on Intro Skipper.

## Defaults

- Analyze new recordings automatically.
- Follow all current Jellyfin DVR recording locations.
- Wait two minutes after completion, then require 60 seconds of file stability.
- Run one job at a time with two Comskip threads.
- Use play-nice mode and a six-hour timeout.
- Use a US OTA profile with `detect_method=43`.
- Force EDL-only output and software decoding.

Additional virtual libraries can be selected on the plugin page. Their stable Jellyfin IDs are retained if a library is temporarily unavailable, and current paths are resolved again during each scan.

A custom `comskip.ini` may override detector settings. The plugin still enforces EDL-only output and disables Comskip hardware decoding.

## How it works

Item events queue completed local recordings, and a six-hour scheduled task reconciles missed work. Active recordings and files outside the selected libraries are ignored.

EDL ranges are validated, clamped to the recording duration, sorted, and merged when overlapping or separated by 250 ms or less. Results are invalidated when the source file or detector configuration changes.

When Recording Transcoder is installed, Commercial Skipper waits for its lease and analyzes the final transcoded file.

## Troubleshooting

Commercial Skipper 1.0.0.0 can prevent Jellyfin 10.11.11 from starting. Version
1.0.0.1 fixes the dependency cycle. If 1.0.0.0 is installed and Jellyfin cannot
start, quit Jellyfin and move that version outside the plugins directory:

```bash
mkdir -p ~/Desktop/Jellyfin-disabled-plugins
mv ~/Library/Application\ Support/jellyfin/plugins/Commercial\ Skipper_1.0.0.0 \
  ~/Desktop/Jellyfin-disabled-plugins/
```

Start Jellyfin, correct the repository URL if necessary, and install 1.0.0.1 or
newer from the Catalog.

If prompts do not appear:

1. Run **Test Comskip** on the plugin page.
2. Run **Scan pending recordings** and review the displayed status.
3. Confirm the recording is in an enabled library and is no longer active.
4. Confirm the user's Commercial media-segment action is **Ask to Skip**.
5. Review the Jellyfin server log and the plugin's retained failed-job diagnostics.

A forced scan ignores the cached result and completion delay. It does not change the recording.

## Admin API

All routes require an authenticated Jellyfin administrator:

- `GET /CommercialSkipper/Libraries`
- `GET /CommercialSkipper/Status`
- `POST /CommercialSkipper/Detector/Test`
- `POST /CommercialSkipper/Scans`
- `DELETE /CommercialSkipper/Jobs/{id}`
- `DELETE /CommercialSkipper/Segments`

Scan body: `{"force":false}`.

## Development

From this directory:

```bash
make restore
make build
make test
make package
make validate-package
```

The release archive is written to `artifacts/commercial-skipper_<version>.zip`.
