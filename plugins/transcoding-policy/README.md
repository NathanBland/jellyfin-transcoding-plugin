# Transcoding Policy

Transcoding Policy selectively chooses Jellyfin's software output encoder for matching transcodes. Its default rule works around the macOS VideoToolbox failure affecting MPEG-2 Live TV sources such as HDHomeRun channels:

```text
VideoToolbox enabled + live MPEG-2 input + H.264 output
→ libx264
```

Nonmatching jobs continue through Jellyfin's normal encoder selection, so ordinary H.264 and HEVC library transcodes can still use VideoToolbox.

Requires Jellyfin Server 10.11.11 / .NET 9. The plugin performs an exact server-version and internal-method check at startup; if either check fails, the patch is not installed and Jellyfin continues normally.

## Install

1. Add the repository URL from the [root README](../../README.md) to Jellyfin.
2. Install **Transcoding Policy** from **Dashboard → Plugins → Catalog**.
3. Restart Jellyfin.
4. Open **Dashboard → Plugins → My Plugins → Transcoding Policy** and confirm that the patch is active.

Installing, updating, or removing the plugin requires a Jellyfin restart. Configuration changes apply to subsequent transcodes without restarting.

## Default rule

- Plugin enabled.
- Software-encoding rule enabled.
- Live streams only.
- Input codec: `mpeg2video`.
- Output codec: `h264`.
- Decision logging enabled.

Codec lists use comma-separated FFmpeg/Jellyfin codec identifiers. Turning off **Live streams only** also allows matching library files.

## Verify

Start an affected HDHomeRun channel and inspect its FFmpeg transcode log. A matching job should contain:

```text
-c:v libx264
```

It should not select `h264_videotoolbox`. The Jellyfin server log also records:

```text
Transcoding Policy selected software encoder libx264
```

## Safety and rollback

The plugin changes only Jellyfin's encoder-selection return value for a matching rule. It does not edit `encoding.xml`, rewrite generated FFmpeg commands, or change global transcoding settings.

If policy evaluation fails, Jellyfin's original selection runs. To roll back, stop Jellyfin, remove the plugin, and start Jellyfin again.

This release controls the output encoder. For the default MPEG-2 rule, Jellyfin also chooses its software processing filters. Forcing a complete software pipeline for H.264 or HEVC input would require additional decoder/filter policy support.

## Manual installation

Use this only when the catalog is unavailable:

1. Stop Jellyfin.
2. Extract the release ZIP into its own directory under Jellyfin's plugin directory.
3. Confirm that directory contains `Jellyfin.Plugin.TranscodingPolicy.dll`.
4. Start Jellyfin and confirm the patch status.

A default native macOS plugin directory is commonly:

```text
~/Library/Application Support/jellyfin/plugins/
```

Manual installations do not receive catalog update discovery.

## Development

From this directory:

```bash
make restore
make build
make test
make package
make validate-package
```

From the repository root:

```bash
make PLUGIN=transcoding-policy ci
```

The release archive and checksums are written to `artifacts/`. Maintainers publish with the **Release Jellyfin Plugin** workflow using a tag such as `transcoding-policy-v1.1.0.0`.
