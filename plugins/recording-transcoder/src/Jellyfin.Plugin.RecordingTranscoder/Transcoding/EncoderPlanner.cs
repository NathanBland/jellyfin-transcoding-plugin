using System.Globalization;
using Jellyfin.Plugin.RecordingTranscoder.Configuration;
using Jellyfin.Plugin.RecordingTranscoder.Models;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.RecordingTranscoder.Transcoding;

public static class EncoderPlanner
{
    public static IReadOnlyList<EncoderCandidateSpec> GetCandidateSpecs(
        EncoderMode mode,
        HardwareAccelerationType hardwareAccelerationType,
        bool enableHardwareEncoding,
        bool allowAv1Encoding,
        bool allowHevcEncoding)
    {
        if (mode is EncoderMode.HevcVideoToolboxMain10 or EncoderMode.HevcVideoToolboxMain)
        {
            mode = EncoderMode.FollowJellyfin;
        }

        var codecs = new List<string>();
        if (allowAv1Encoding)
        {
            codecs.Add("av1");
        }

        if (allowHevcEncoding)
        {
            codecs.Add("hevc");
        }

        codecs.Add("h264");
        var candidates = new List<EncoderCandidateSpec>();
        var hardwareEnabled = enableHardwareEncoding && hardwareAccelerationType != HardwareAccelerationType.none;
        var backend = hardwareAccelerationType.ToString();
        if ((mode is EncoderMode.FollowJellyfin or EncoderMode.HardwareOnly) && hardwareEnabled)
        {
            candidates.AddRange(codecs.Select(codec => new EncoderCandidateSpec(
                $"{codec}_{backend}",
                codec,
                true,
                backend)));
        }

        if (mode is EncoderMode.FollowJellyfin or EncoderMode.SoftwareOnly)
        {
            candidates.AddRange(codecs.Select(codec => new EncoderCandidateSpec(
                codec switch
                {
                    "av1" => "libsvtav1",
                    "hevc" => "libx265",
                    _ => "libx264"
                },
                codec,
                false,
                "software")));
        }

        return candidates;
    }

    public static bool IsInterlaced(ProbeStream video)
        => video.FieldOrder is not ("" or "unknown" or "progressive");

    public static bool IsEfficientInputCodec(string codec)
        => codec is "h264" or "hevc" or "av1";

    public static long CalculateTargetBitRate(ProbeStream video, double multiplier)
    {
        var frameRate = Math.Max(1, video.FramesPerSecond) * (IsInterlaced(video) ? 2 : 1);
        var calculated = video.Width * (double)video.Height * frameRate * 0.055 * Math.Clamp(multiplier, 0.25, 4.0);
        return (long)Math.Clamp(calculated, 1_500_000, 15_000_000);
    }

    public static IReadOnlyList<string> BuildArguments(
        string sourcePath,
        string outputPath,
        MediaProbe probe,
        EncoderCapabilities capabilities,
        PluginConfiguration configuration)
    {
        var video = probe.Video ?? throw new InvalidOperationException("Input has no video stream.");
        var selection = capabilities.Selected
            ?? throw new InvalidOperationException("No supported encoder is available.");
        var targetBitRate = CalculateTargetBitRate(video, configuration.BitrateMultiplier);
        var outputFrameRate = Math.Max(1, video.FramesPerSecond) * (IsInterlaced(video) ? 2 : 1);
        var gop = Math.Max(12, (int)Math.Round(outputFrameRate * 2, MidpointRounding.AwayFromZero));
        var filter = BuildFilter(video, selection);

        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y" };
        arguments.AddRange(selection.InitializationArguments);
        arguments.AddRange([
            "-i", sourcePath,
            "-map", "0:v:0",
            "-map", "0:a?",
            "-map", "0:s?",
            "-map", "0:t?",
            "-map_metadata", "0",
            "-map_chapters", "0",
            "-c:a", "copy",
            "-c:s", "copy",
            "-c:t", "copy",
            "-c:v:0", selection.Encoder
        ]);
        if (!selection.RequiresHardwareUpload)
        {
            arguments.AddRange(["-pix_fmt", selection.PixelFormat]);
        }

        arguments.AddRange([
            "-b:v", targetBitRate.ToString(CultureInfo.InvariantCulture),
            "-maxrate", ((long)(targetBitRate * 1.5)).ToString(CultureInfo.InvariantCulture),
            "-bufsize", (targetBitRate * 2).ToString(CultureInfo.InvariantCulture),
            "-g", gop.ToString(CultureInfo.InvariantCulture),
            "-vf", filter
        ]);
        AddEncoderSpecificArguments(arguments, selection);
        if (selection.SupportsA53ClosedCaptions && selection.Codec is "h264" or "hevc")
        {
            arguments.AddRange(["-a53cc", "1"]);
        }

        arguments.AddRange(["-copyts", "-start_at_zero"]);
        AddColorMetadata(arguments, video);
        arguments.AddRange(["-f", "mpegts", outputPath]);
        return arguments;
    }

    internal static IReadOnlyList<string> BuildEncoderTestArguments(EncoderSelection selection, string outputPath)
    {
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-v", "error", "-y" };
        arguments.AddRange(selection.InitializationArguments);
        arguments.AddRange([
            "-f", "lavfi",
            "-i", "color=c=black:s=320x180:r=30:d=0.2",
            "-frames:v", "1",
            "-an",
            "-c:v", selection.Encoder,
            "-vf", selection.RequiresHardwareUpload
                ? $"format={selection.PixelFormat},hwupload=extra_hw_frames=16"
                : $"format={selection.PixelFormat}"
        ]);
        if (!selection.RequiresHardwareUpload)
        {
            arguments.AddRange(["-pix_fmt", selection.PixelFormat]);
        }

        arguments.AddRange(["-b:v", "1000000", "-g", "60"]);
        AddEncoderSpecificArguments(arguments, selection);
        arguments.AddRange(["-f", "mpegts", outputPath]);
        return arguments;
    }

    private static string BuildFilter(ProbeStream video, EncoderSelection selection)
    {
        var filters = new List<string>();
        if (IsInterlaced(video))
        {
            filters.Add("bwdif=mode=send_field:parity=auto:deint=interlaced");
        }

        filters.Add($"format={selection.PixelFormat}");
        var setParams = BuildColorSetParams(video);
        if (setParams is not null)
        {
            filters.Add($"setparams={setParams}");
        }

        if (selection.RequiresHardwareUpload)
        {
            filters.Add("hwupload=extra_hw_frames=64");
        }

        return string.Join(',', filters);
    }

    private static void AddEncoderSpecificArguments(List<string> arguments, EncoderSelection selection)
    {
        if (selection.Codec == "hevc" && !string.IsNullOrWhiteSpace(selection.Profile))
        {
            arguments.AddRange(["-profile:v", selection.Profile]);
        }

        switch (selection.Backend)
        {
            case "videotoolbox":
                arguments.AddRange(["-allow_sw", "0", "-realtime", "0"]);
                if (selection.SupportsPrioritySpeed)
                {
                    arguments.AddRange(["-prio_speed", "0"]);
                }

                if (selection.SupportsPowerEfficient)
                {
                    arguments.AddRange(["-power_efficient", "1"]);
                }

                if (selection.SupportsSpatialAq)
                {
                    arguments.AddRange(["-spatial_aq", "1"]);
                }

                break;
            case "vaapi":
                arguments.AddRange(["-rc_mode", "VBR"]);
                break;
            case "software" when selection.Encoder is "libx264" or "libx265":
                arguments.AddRange(["-preset", "slow"]);
                break;
            case "software" when selection.Encoder == "libsvtav1":
                arguments.AddRange(["-preset", "6"]);
                break;
        }
    }

    private static void AddColorMetadata(List<string> arguments, ProbeStream video)
    {
        var isHd = video.Width >= 1280 || video.Height >= 720;
        var colorSpace = NormalizeColorValue(video.ColorSpace, isHd ? "bt709" : null);
        var colorTransfer = NormalizeColorValue(video.ColorTransfer, isHd ? "bt709" : null);
        var colorPrimaries = NormalizeColorValue(video.ColorPrimaries, isHd ? "bt709" : null);
        var colorRange = NormalizeColorValue(video.ColorRange, null);

        if (colorSpace is not null)
        {
            arguments.AddRange(["-colorspace:v", colorSpace]);
        }

        if (colorTransfer is not null)
        {
            arguments.AddRange(["-color_trc:v", colorTransfer]);
        }

        if (colorPrimaries is not null)
        {
            arguments.AddRange(["-color_primaries:v", colorPrimaries]);
        }

        if (colorRange is not null)
        {
            arguments.AddRange(["-color_range:v", colorRange]);
        }
    }

    private static string? BuildColorSetParams(ProbeStream video)
    {
        var isHd = video.Width >= 1280 || video.Height >= 720;
        var values = new List<string>();
        var colorSpace = NormalizeColorValue(video.ColorSpace, isHd ? "bt709" : null);
        var colorTransfer = NormalizeColorValue(video.ColorTransfer, isHd ? "bt709" : null);
        var colorPrimaries = NormalizeColorValue(video.ColorPrimaries, isHd ? "bt709" : null);
        var colorRange = NormalizeColorValue(video.ColorRange, null) switch
        {
            "tv" => "limited",
            "pc" => "full",
            var value => value
        };

        if (colorRange is not null)
        {
            values.Add($"range={colorRange}");
        }

        if (colorPrimaries is not null)
        {
            values.Add($"color_primaries={colorPrimaries}");
        }

        if (colorTransfer is not null)
        {
            values.Add($"color_trc={colorTransfer}");
        }

        if (colorSpace is not null)
        {
            values.Add($"colorspace={colorSpace}");
        }

        return values.Count == 0 ? null : string.Join(':', values);
    }

    private static string? NormalizeColorValue(string value, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            ? value
            : fallback;
    }
}
