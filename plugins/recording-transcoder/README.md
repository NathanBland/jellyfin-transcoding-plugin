# Recording Transcoder

Recording Transcoder recompresses completed Jellyfin DVR transport streams with Jellyfin's configured FFmpeg. A validated output replaces the raw stream at the same path, preserving Jellyfin's recording identity, sidecars, artwork, and retention bookkeeping.

It supports Jellyfin 10.11.11 / .NET 9 on Linux, Windows, and macOS. Hardware acceleration is optional.

> Automatic transcoding is disabled on first installation. Successful jobs eventually delete the raw source. Keep a separate backup during rollout and test only noncritical recordings before enabling automation.

## Install and test safely

1. Add the repository URL from the [root README](../../README.md) to Jellyfin.
2. Install **Recording Transcoder** from **Dashboard → Plugins → Catalog**.
3. Restart Jellyfin.
4. Open **Dashboard → Plugins → My Plugins → Recording Transcoder**.
5. Leave **Automatically transcode completed recordings** disabled.
6. Select **Follow Jellyfin**, save, and select **Test encoder**.
7. Confirm the selected encoder and whether it is hardware or software.
8. For an isolated test, turn off **Always follow Jellyfin DVR recording libraries** and select a dedicated library containing a noncritical recording.
9. Select **Scan eligible recordings**.
10. Verify playback, audio, captions, duration, dimensions, frame rate, file size, metadata, and commercial ranges.
11. Restore the intended library scope and enable automatic transcoding only after successful testing.

A forced scan can re-encode H.264, HEVC, or AV1 recordings. Use it only when that behavior is intentional.

## Encoder policy

The plugin always uses the FFmpeg and FFprobe paths exposed by Jellyfin's `IMediaEncoder` service.

### Follow Jellyfin (default)

The plugin reads Jellyfin's current transcoding configuration on every job:

- selected hardware acceleration backend;
- **Enable hardware encoding**;
- **Allow encoding in AV1 format**;
- **Allow encoding in HEVC format**;
- configured VAAPI or QSV device path.

When hardware encoding is enabled, candidates follow Jellyfin's backend suffix and the permitted output formats:

```text
AV1 hardware, when AV1 output is allowed
→ HEVC hardware, when HEVC output is allowed
→ H.264 hardware
```

Each candidate must be advertised by Jellyfin FFmpeg and complete a real one-frame MPEG-TS encode/probe test. If no permitted hardware candidate works, the plugin tries Jellyfin FFmpeg's corresponding software encoders:

```text
libsvtav1 → libx265 → libx264
```

If Jellyfin hardware encoding is disabled, the plugin starts with that software sequence. It never uses a system FFmpeg path in place of Jellyfin's configured binary.

### Other policies

- **Require Jellyfin hardware encoding:** use only the configured Jellyfin hardware backend; retain the raw source if no candidate passes its session test.
- **Use software encoding:** use only `libsvtav1`, `libx265`, or `libx264` according to Jellyfin's AV1/HEVC output permissions.

Supported Jellyfin backend mappings include:

| Jellyfin backend | FFmpeg encoder suffix |
|---|---|
| AMD AMF | `_amf` |
| Intel Quick Sync | `_qsv` |
| NVIDIA NVENC | `_nvenc` |
| VAAPI | `_vaapi` |
| Apple VideoToolbox | `_videotoolbox` |
| V4L2 M2M | `_v4l2m2m` |
| Rockchip MPP | `_rkmpp` |

Unsupported codec/backend combinations are skipped automatically. For example, a backend that exposes only H.264 falls through to its H.264 encoder.

## Encoding behavior

Recording input is decoded in software. Interlaced input uses software `bwdif` in `send_field` mode before frames are uploaded when VAAPI or QSV encoding is selected. This keeps MPEG-2 decoding and deinterlacing consistent across operating systems and avoids coupling the post-processing job to Jellyfin's live-playback filter graph.

The output uses:

- AV1, HEVC, or H.264 selected from Jellyfin's settings;
- 10-bit output when the selected encoder advertises a compatible 10-bit pixel format;
- a resolution/frame-rate VBR target clamped to 1.5–15 Mbps;
- a 1.5× maximum rate, 2× buffer, and two-second GOP;
- copied audio, subtitle, attachment, metadata, and chapter streams;
- source color metadata, with BT.709 used when HD metadata is missing;
- hardware-only VideoToolbox sessions with `allow_sw=0` when VideoToolbox is selected;
- configured VAAPI/QSV devices and hardware upload when those backends are selected.

Normal scans skip recordings already encoded as H.264, HEVC, or AV1 to avoid unnecessary generation loss. The output remains MPEG-TS at the original `.ts` path. AV1 is selected only after Jellyfin FFmpeg successfully writes and probes a test MPEG-TS stream, but client support should still be verified before enabling automation.

## Replacement safety

Before publishing an output, the plugin:

1. Waits for recording completion and file stability.
2. Probes the source streams, duration, interlace state, captions, bitrate, and available disk space.
3. Requires estimated output space plus 1 GiB.
4. Encodes a hidden MPEG-TS file on the same filesystem.
5. Requires the selected codec/profile, dimensions, deinterlaced frame rate, color metadata, and duration.
6. Requires exact audio, subtitle, and attachment stream-count parity.
7. Requires embedded closed captions to remain present when FFprobe identifies them.
8. Requires at least 20% savings by default.
9. Rechecks that the source did not change during encoding.
10. Publishes through a journaled rename, verifies FFprobe and Jellyfin path resolution, and only then removes the raw backup.

Failures, timeouts, cancellations, and failed validation retain or restore the raw recording and remove incomplete output. Restart recovery restores an uncommitted raw backup, finishes a durably committed output, and never deletes the only remaining copy.

### Closed captions

Some encoders do not carry embedded A/53 captions into the selected output codec. If FFprobe sees captions on the source but not the output, the plugin rejects the output and retains the raw recording. Separate subtitle streams are copied and validated.

Test this with each recording source before enabling automatic jobs. A recording with embedded captions may remain raw by design.

## Commercial Skipper coordination

Recording Transcoder claims a shared pipeline lease when a recording is queued. Commercial Skipper waits and analyzes the final published file after Jellyfin receives the item update. Either plugin can be used independently.

## Troubleshooting

- **Unexpected software encoder:** inspect Jellyfin's hardware backend, **Enable hardware encoding**, AV1/HEVC output permissions, and the fallback reason shown by the plugin.
- **No encoder selected:** use **Test encoder**. The status identifies absent FFmpeg encoders and failed session tests.
- **VAAPI or QSV failure:** confirm Jellyfin's configured render device exists and the Jellyfin process can access it.
- **Job skipped:** normal scans skip H.264, HEVC, and AV1 sources.
- **Validation failed:** read the plugin status and Jellyfin server log; the raw file remains untouched or is restored.
- **Insufficient savings:** lower the bitrate multiplier or minimum-savings requirement only after checking output quality.
- **Caption validation failed:** retain the raw source or provide captions as a separate subtitle stream.

## Admin API

All routes require an authenticated Jellyfin administrator:

- `GET /RecordingTranscoder/Libraries`
- `GET /RecordingTranscoder/Capabilities`
- `GET /RecordingTranscoder/Status`
- `POST /RecordingTranscoder/Encoder/Test`
- `POST /RecordingTranscoder/Scans`
- `DELETE /RecordingTranscoder/Jobs/{id}`

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

The release archive is written to `artifacts/recording-transcoder_<version>.zip`.
