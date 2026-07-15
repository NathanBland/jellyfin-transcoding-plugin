using Jellyfin.Plugin.TranscodingPolicy.Configuration;
using Jellyfin.Plugin.TranscodingPolicy.Policy;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.TranscodingPolicy.Tests;

public sealed class TranscodingPolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_DefaultMpeg2LiveTvJob_ForcesSoftwareEncoder()
    {
        var decision = TranscodingPolicyEvaluator.Evaluate(
            CreateState("mpeg2video", isLive: true),
            CreateVideoToolboxOptions(),
            "h264",
            new PluginConfiguration());

        Assert.True(decision.ForceSoftwareEncoder);
        Assert.Contains("mpeg2video", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("h264", true, "h264")]
    [InlineData("mpeg2video", false, "h264")]
    [InlineData("mpeg2video", true, "hevc")]
    public void Evaluate_NonmatchingJob_UsesJellyfinDefault(string inputCodec, bool isLive, string outputCodec)
    {
        var decision = TranscodingPolicyEvaluator.Evaluate(
            CreateState(inputCodec, isLive),
            CreateVideoToolboxOptions(),
            outputCodec,
            new PluginConfiguration());

        Assert.False(decision.ForceSoftwareEncoder);
    }

    [Fact]
    public void Evaluate_LibraryMatchingEnabled_ForcesSoftwareEncoder()
    {
        var configuration = new PluginConfiguration
        {
            LiveStreamsOnly = false
        };

        var decision = TranscodingPolicyEvaluator.Evaluate(
            CreateState("mpeg2video", isLive: false),
            CreateVideoToolboxOptions(),
            "h264",
            configuration);

        Assert.True(decision.ForceSoftwareEncoder);
    }

    [Fact]
    public void Evaluate_NonVideoToolboxJob_UsesJellyfinDefault()
    {
        var options = CreateVideoToolboxOptions();
        options.HardwareAccelerationType = HardwareAccelerationType.none;

        var decision = TranscodingPolicyEvaluator.Evaluate(
            CreateState("mpeg2video", isLive: true),
            options,
            "h264",
            new PluginConfiguration());

        Assert.False(decision.ForceSoftwareEncoder);
    }

    [Fact]
    public void Evaluate_CodecMatching_IsCaseInsensitiveAndTrimsConfiguration()
    {
        var configuration = new PluginConfiguration
        {
            InputCodecs = [" MPEG2VIDEO "],
            OutputCodecs = [" H264 "]
        };

        var decision = TranscodingPolicyEvaluator.Evaluate(
            CreateState("mpeg2video", isLive: true),
            CreateVideoToolboxOptions(),
            "h264",
            configuration);

        Assert.True(decision.ForceSoftwareEncoder);
    }

    [Fact]
    public void Evaluate_DisabledConfiguration_UsesJellyfinDefault()
    {
        var configuration = new PluginConfiguration
        {
            IsEnabled = false
        };

        var decision = TranscodingPolicyEvaluator.Evaluate(
            CreateState("mpeg2video", isLive: true),
            CreateVideoToolboxOptions(),
            "h264",
            configuration);

        Assert.False(decision.ForceSoftwareEncoder);
    }

    internal static EncodingJobInfo CreateState(string inputCodec, bool isLive)
        => new(TranscodingJobType.Hls)
        {
            VideoStream = new MediaStream { Codec = inputCodec },
            BaseRequest = new BaseEncodingJobOptions
            {
                LiveStreamId = isLive ? "live-stream-id" : string.Empty
            },
            MediaSource = new MediaSourceInfo
            {
                LiveStreamId = isLive ? "live-stream-id" : string.Empty
            }
        };

    internal static EncodingOptions CreateVideoToolboxOptions()
        => new()
        {
            HardwareAccelerationType = HardwareAccelerationType.videotoolbox,
            EnableHardwareEncoding = true
        };
}

