using Jellyfin.Plugin.RecordingTranscoder.Configuration;
using Jellyfin.Plugin.RecordingTranscoder.Models;
using Jellyfin.Plugin.RecordingTranscoder.Transcoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.RecordingTranscoder.Tests;

public sealed class EncoderPlannerTests
{
    [Fact]
    public void FollowJellyfin_UsesConfiguredHardwareThenSoftwareFallbacks()
    {
        var candidates = EncoderPlanner.GetCandidateSpecs(
            EncoderMode.FollowJellyfin,
            HardwareAccelerationType.nvenc,
            true,
            true,
            true);

        Assert.Equal(
            ["av1_nvenc", "hevc_nvenc", "h264_nvenc", "libsvtav1", "libx265", "libx264"],
            candidates.Select(candidate => candidate.Encoder));
        Assert.All(candidates.Take(3), candidate => Assert.True(candidate.IsHardware));
        Assert.All(candidates.Skip(3), candidate => Assert.False(candidate.IsHardware));
    }

    [Fact]
    public void FollowJellyfin_WhenHardwareDisabled_UsesSoftwareOnly()
    {
        var candidates = EncoderPlanner.GetCandidateSpecs(
            EncoderMode.FollowJellyfin,
            HardwareAccelerationType.videotoolbox,
            false,
            false,
            true);

        Assert.Equal(["libx265", "libx264"], candidates.Select(candidate => candidate.Encoder));
        Assert.All(candidates, candidate => Assert.False(candidate.IsHardware));
        Assert.Empty(EncoderPlanner.GetCandidateSpecs(
            EncoderMode.HardwareOnly,
            HardwareAccelerationType.videotoolbox,
            false,
            false,
            true));
    }

    [Theory]
    [InlineData(HardwareAccelerationType.amf, "hevc_amf")]
    [InlineData(HardwareAccelerationType.qsv, "hevc_qsv")]
    [InlineData(HardwareAccelerationType.nvenc, "hevc_nvenc")]
    [InlineData(HardwareAccelerationType.v4l2m2m, "hevc_v4l2m2m")]
    [InlineData(HardwareAccelerationType.vaapi, "hevc_vaapi")]
    [InlineData(HardwareAccelerationType.videotoolbox, "hevc_videotoolbox")]
    [InlineData(HardwareAccelerationType.rkmpp, "hevc_rkmpp")]
    public void HardwareBackends_UseJellyfinEncoderSuffix(HardwareAccelerationType backend, string expected)
    {
        var candidates = EncoderPlanner.GetCandidateSpecs(EncoderMode.HardwareOnly, backend, true, false, true);
        Assert.Equal(expected, candidates[0].Encoder);
    }

    [Fact]
    public void DeviceInitialization_UsesJellyfinVaapiAndQsvPaths()
    {
        var options = new EncodingOptions
        {
            VaapiDevice = "/dev/dri/renderD130",
            QsvDevice = "/dev/dri/renderD131"
        };

        var vaapi = RecordingTranscoderRunner.BuildInitializationArguments("vaapi", options);
        var qsvLinux = RecordingTranscoderRunner.BuildInitializationArguments("qsv", options, true);
        var qsvOther = RecordingTranscoderRunner.BuildInitializationArguments("qsv", options, false);

        Assert.Contains("vaapi=recording:/dev/dri/renderD130", vaapi);
        Assert.Contains("vaapi=recording_va:/dev/dri/renderD131", qsvLinux);
        Assert.Contains("qsv=recording@recording_va", qsvLinux);
        Assert.Contains("qsv=recording:/dev/dri/renderD131", qsvOther);
    }

    [Fact]
    public void CalculateTargetBitRate_Interlaced1080_UsesDoubledTemporalRate()
    {
        var video = Video(1920, 1080, 30000d / 1001, "tt");
        var result = EncoderPlanner.CalculateTargetBitRate(video, 1.0);
        Assert.InRange(result, 6_800_000, 6_900_000);
    }

    [Fact]
    public void BuildArguments_VideoToolboxMain10_PreservesStreamsAndUsesHardwareOnly()
    {
        var selection = Selection("hevc_videotoolbox", "hevc", true, "videotoolbox", "p010le", "main10");
        var probe = new MediaProbe(3600, 15_000_000, [Video(1920, 1080, 30000d / 1001, "tt")]);
        var arguments = EncoderPlanner.BuildArguments("show.ts", ".show.tmp", probe, Capabilities(selection), new PluginConfiguration()).ToArray();

        Assert.Contains("hevc_videotoolbox", arguments);
        Assert.Contains("main10", arguments);
        Assert.Contains("p010le", arguments);
        Assert.Contains("0:a?", arguments);
        Assert.Contains("0:t?", arguments);
        var filter = arguments[Array.IndexOf(arguments, "-vf") + 1];
        Assert.Contains("bwdif=mode=send_field:parity=auto:deint=interlaced,format=p010le", filter, StringComparison.Ordinal);
        Assert.Contains("setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709", filter, StringComparison.Ordinal);
        Assert.Equal("0", arguments[Array.IndexOf(arguments, "-allow_sw") + 1]);
        Assert.Equal("bt709", arguments[Array.IndexOf(arguments, "-colorspace:v") + 1]);
    }

    [Fact]
    public void BuildArguments_Vaapi_InitializesDeviceAndUploadsSoftwareFrames()
    {
        var selection = Selection(
            "hevc_vaapi",
            "hevc",
            true,
            "vaapi",
            "nv12",
            "main",
            ["-init_hw_device", "vaapi=recording:/dev/dri/renderD128", "-filter_hw_device", "recording"],
            true);
        var probe = new MediaProbe(1800, 8_000_000, [Video(1280, 720, 30, "progressive")]);
        var arguments = EncoderPlanner.BuildArguments("show.ts", ".show.tmp", probe, Capabilities(selection), new PluginConfiguration()).ToArray();

        Assert.True(Array.IndexOf(arguments, "-init_hw_device") < Array.IndexOf(arguments, "-i"));
        Assert.Contains("hwupload=extra_hw_frames=64", arguments[Array.IndexOf(arguments, "-vf") + 1], StringComparison.Ordinal);
        Assert.DoesNotContain("-pix_fmt", arguments);
        Assert.Equal("VBR", arguments[Array.IndexOf(arguments, "-rc_mode") + 1]);
    }

    [Fact]
    public void BuildArguments_SoftwareProgressive_UsesPortableTenBitEncoder()
    {
        var selection = Selection("libx265", "hevc", false, "software", "yuv420p10le", "main10");
        var probe = new MediaProbe(1800, 8_000_000, [Video(1280, 720, 30, "progressive")]);
        var arguments = EncoderPlanner.BuildArguments("show.ts", ".show.tmp", probe, Capabilities(selection), new PluginConfiguration()).ToArray();

        var filter = arguments[Array.IndexOf(arguments, "-vf") + 1];
        Assert.DoesNotContain("bwdif", filter, StringComparison.Ordinal);
        Assert.Contains("format=yuv420p10le", filter, StringComparison.Ordinal);
        Assert.Equal("slow", arguments[Array.IndexOf(arguments, "-preset") + 1]);
        Assert.DoesNotContain("-allow_sw", arguments);
    }

    [Fact]
    public void Configuration_DefaultsToSafeFollowJellyfinPolicy()
    {
        var configuration = new PluginConfiguration();
        Assert.False(configuration.AutomaticTranscoding);
        Assert.Equal(EncoderMode.FollowJellyfin, configuration.Encoder);
    }

    [Theory]
    [InlineData("mpeg2video", false)]
    [InlineData("h264", true)]
    [InlineData("hevc", true)]
    [InlineData("av1", true)]
    public void EfficientInputCodecs_AreSkippedByNormalScans(string codec, bool expected)
        => Assert.Equal(expected, EncoderPlanner.IsEfficientInputCodec(codec));

    private static EncoderCapabilities Capabilities(EncoderSelection selection) => new(
        "/usr/bin/ffmpeg",
        "/usr/bin/ffprobe",
        selection.Backend,
        selection.IsHardware,
        false,
        true,
        [selection.Encoder],
        selection,
        true,
        !selection.IsHardware,
        null,
        null,
        string.Empty);

    internal static EncoderSelection Selection(
        string encoder,
        string codec,
        bool hardware,
        string backend,
        string pixelFormat,
        string? profile,
        IReadOnlyList<string>? initializationArguments = null,
        bool requiresHardwareUpload = false)
        => new(
            encoder,
            codec,
            hardware,
            backend,
            pixelFormat,
            profile,
            initializationArguments ?? [],
            requiresHardwareUpload,
            true,
            true,
            true,
            true);

    private static ProbeStream Video(int width, int height, double fps, string fieldOrder)
        => new(0, "video", "mpeg2video", "Main", width, height, fps, fieldOrder, 12_000_000, false, "bt709", "bt709", "bt709", "tv");
}
