# Jellyfin Transcoding Policy

`Transcoding Policy` is a Jellyfin 10.11.11 server plugin that selectively
forces Jellyfin's existing software output encoder for matching transcodes.

Its default policy works around a macOS VideoToolbox failure affecting MPEG-2
Live TV sources such as HDHomeRun channels:

```text
VideoToolbox selected
+ hardware encoding enabled
+ live stream
+ MPEG-2 input
+ H.264 output
→ libx264
```

All nonmatching jobs use Jellyfin's unmodified encoder selection. H.264 and
HEVC library transcodes therefore continue to use VideoToolbox when Jellyfin
would normally select it.

## Compatibility

- Jellyfin Server: **10.11.11 only**
- Target framework: .NET 9
- Intended platform: macOS with Apple VideoToolbox
- Development dependency: .NET 9 SDK

The plugin performs an exact server-version and private-method signature check
at startup. If either check fails, the runtime patch is not installed and
Jellyfin continues normally.

## Recommended installation: Jellyfin repository

Install and manage the plugin through Jellyfin's plugin system:

1. Open **Dashboard → Plugins → Repositories**.
2. Add a repository with these values:

   ```text
   Repository name: Transcoding Policy
   Repository URL:  https://raw.githubusercontent.com/NathanBland/jellyfin-transcoding-plugin/manifest/manifest.json
   ```

3. Save the repository.
4. Open **Dashboard → Plugins → Catalog**, find **Transcoding Policy**, and
   install it.
5. Restart Jellyfin to load the plugin.
6. Open **Dashboard → Plugins → My Plugins → Transcoding Policy** to confirm
   the patch is active and review its settings.

This is the preferred installation method because Jellyfin can discover new
compatible releases through the same repository. The GitHub repository must
remain public so Jellyfin can fetch the manifest and release archive without
GitHub credentials.

## Build and test

Install a .NET 9 SDK, then run:

```bash
make restore
make build
make test
make package
```

The release archive plus SHA-256 and MD5 checksums are written to `artifacts/`.
The archive contains the two DLLs at its root, which is the layout expected by
Jellyfin's catalog installer.

## Manual installation fallback

Use this only when the Jellyfin repository/catalog method is unavailable.
Manual installations do not receive release discovery through the catalog.

1. Stop Jellyfin.
2. Extract the release archive into a subdirectory under the active Jellyfin
   plugins directory. A default native macOS installation commonly uses:

   ```text
   ~/Library/Application Support/jellyfin/plugins/
   ```

3. Confirm the plugin directory contains both:

   ```text
   Jellyfin.Plugin.TranscodingPolicy.dll
   0Harmony.dll
   ```

4. Start Jellyfin.
5. Open **Dashboard → Plugins → Transcoding Policy** and confirm the patch is
   active.

Installing, removing, or updating the plugin requires a Jellyfin restart.
Configuration changes affect subsequent transcodes without restarting.

## Automated releases

The repository has two GitHub Actions workflows:

- `CI` builds, tests, packages, and validates every change.
- `Release Plugin` creates the GitHub release and updates an installable
  `manifest.json` on the dedicated `manifest` branch.

To publish a release, open **Actions → Release Plugin → Run workflow** and enter
a four-part tag such as `v1.1.0.0`. The workflow validates the version, creates
the tag, uploads the flat ZIP and checksums, and updates the `manifest` branch.
Pushing an annotated four-part version tag triggers the same release path.

The workflow uses the built-in `GITHUB_TOKEN`; no repository secret is needed.
If release or branch creation is denied, enable **Read and write permissions**
under **Settings → Actions → General → Workflow permissions**.

For a later release, update the four-part version in both `build.yaml` and the
plugin `.csproj`, commit it, and run the release workflow with the matching
`v<version>` tag. Existing entries are retained in the catalog so Jellyfin can
see release history and upgrades.

## Default configuration

- Plugin enabled: yes
- Software-encoding rule enabled: yes
- Live streams only: yes
- Input codecs: `mpeg2video`
- Output codecs: `h264`
- Decision logging: yes

Codec lists are comma-separated FFmpeg/Jellyfin codec identifiers. Turning off
"Live streams only" also applies the rule to matching library files.

## Verify the workaround

Start an affected HDHomeRun channel, then inspect the newest FFmpeg transcode
log. A matching job should contain:

```text
-c:v libx264
```

It should no longer select `h264_videotoolbox`. Because VideoToolbox does not
decode the MPEG-2 source, Jellyfin 10.11.11 will also choose its software filter
chain for that job.

The Jellyfin server log records a message beginning with:

```text
Transcoding Policy selected software encoder libx264
```

## Safety and rollback

The patch changes only the return value of Jellyfin's encoder-selection method
when the configured rule matches. It does not edit `encoding.xml`, rewrite
FFmpeg command strings, or mutate global transcoding settings.

If evaluation throws an exception, the prefix returns control to Jellyfin. To
roll back, stop Jellyfin, remove the plugin directory, and start Jellyfin again.

## Current scope

This release controls the output encoder. For the default MPEG-2 rule, that is
also enough to make Jellyfin use software processing filters. A future rule
that forces a complete software pipeline for H.264 or HEVC input would require
additional, separately validated decoder and hardware-surface patches.
