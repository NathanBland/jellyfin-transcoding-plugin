using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.RecordingPipeline;
using Jellyfin.Plugin.RecordingTranscoder.Models;
using MediaBrowser.Controller.MediaEncoding;

namespace Jellyfin.Plugin.RecordingTranscoder.Transcoding;

public sealed class MediaProbeService(IMediaEncoder mediaEncoder, ProcessRunner processRunner)
{
    public async Task<MediaProbe> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            mediaEncoder.ProbePath,
            ["-v", "error", "-show_format", "-show_streams", "-of", "json", path],
            TimeSpan.FromMinutes(2),
            null,
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"FFprobe failed for {path}: {result.StandardError}");
        }

        return Parse(result.StandardOutput);
    }

    internal static MediaProbe Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var streams = new List<ProbeStream>();
        if (root.TryGetProperty("streams", out var streamArray))
        {
            foreach (var stream in streamArray.EnumerateArray())
            {
                streams.Add(new ProbeStream(
                    GetInt(stream, "index"),
                    GetString(stream, "codec_type"),
                    GetString(stream, "codec_name"),
                    GetString(stream, "profile"),
                    GetInt(stream, "width"),
                    GetInt(stream, "height"),
                    ParseRate(GetString(stream, "avg_frame_rate")),
                    GetString(stream, "field_order"),
                    GetLong(stream, "bit_rate"),
                    GetInt(stream, "closed_captions") > 0,
                    GetString(stream, "color_space"),
                    GetString(stream, "color_transfer"),
                    GetString(stream, "color_primaries"),
                    GetString(stream, "color_range")));
            }
        }

        var duration = 0d;
        var bitRate = 0L;
        if (root.TryGetProperty("format", out var format))
        {
            duration = GetDouble(format, "duration");
            bitRate = GetLong(format, "bit_rate");
        }

        return new MediaProbe(duration, bitRate, streams);
    }

    internal static double ParseRate(string value)
    {
        var parts = value.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && denominator != 0)
        {
            return numerator / denominator;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.ToString() : string.Empty;

    private static int GetInt(JsonElement element, string name)
        => int.TryParse(GetString(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static long GetLong(JsonElement element, string name)
        => long.TryParse(GetString(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static double GetDouble(JsonElement element, string name)
        => double.TryParse(GetString(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
}
