using Jellyfin.Plugin.RecordingTranscoder.Configuration;
using Jellyfin.Plugin.RecordingTranscoder.Models;
using Jellyfin.Plugin.RecordingTranscoder.Transcoding;

namespace Jellyfin.Plugin.RecordingTranscoder.Tests;

public sealed class TranscodeValidationTests
{
    [Fact]
    public void ValidateProbes_AcceptsMain10DeinterlacedOutputWithMatchingStreams()
    {
        var input = Probe(Video("mpeg2video", "Main", 29.97, "tt", true), Audio(), Subtitle(), Attachment());
        var output = Probe(Video("hevc", "Main 10", 59.94, "progressive", true), Audio(), Subtitle(), Attachment());

        var result = RecordingTranscoderRunner.ValidateProbes(input, output, 1_000_000, 700_000, new PluginConfiguration(), Main10());

        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    public void ValidateProbes_RejectsCaptionLoss()
    {
        var input = Probe(Video("mpeg2video", "Main", 29.97, "tt", true), Audio());
        var output = Probe(Video("hevc", "Main 10", 59.94, "progressive", false), Audio());

        var result = RecordingTranscoderRunner.ValidateProbes(input, output, 1_000_000, 700_000, new PluginConfiguration(), Main10());

        Assert.False(result.IsValid);
        Assert.Contains("closed captions", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateProbes_RejectsDurationStreamProfileFrameRateAndSavingsFailures()
    {
        var input = Probe(Video("mpeg2video", "Main", 29.97, "tt", false), Audio(), Subtitle());
        var validVideo = Video("hevc", "Main 10", 59.94, "progressive", false);
        var configuration = new PluginConfiguration();

        Assert.False(RecordingTranscoderRunner.ValidateProbes(input, Probe(validVideo, Audio(), Subtitle()) with { DurationSeconds = 95 }, 1_000_000, 700_000, configuration, Main10()).IsValid);
        Assert.False(RecordingTranscoderRunner.ValidateProbes(input, Probe(validVideo, Audio()), 1_000_000, 700_000, configuration, Main10()).IsValid);
        Assert.False(RecordingTranscoderRunner.ValidateProbes(input, Probe(validVideo with { Profile = "Main" }, Audio(), Subtitle()), 1_000_000, 700_000, configuration, Main10()).IsValid);
        Assert.False(RecordingTranscoderRunner.ValidateProbes(input, Probe(validVideo with { FramesPerSecond = 29.97 }, Audio(), Subtitle()), 1_000_000, 700_000, configuration, Main10()).IsValid);
        Assert.False(RecordingTranscoderRunner.ValidateProbes(input, Probe(validVideo, Audio(), Subtitle()), 1_000_000, 900_000, configuration, Main10()).IsValid);
    }

    [Fact]
    public void ValidateProbes_RequiresTheSelectedCodec()
    {
        var input = Probe(Video("mpeg2video", "Main", 30, "progressive", false), Audio());
        var output = Probe(Video("h264", "High", 30, "progressive", false), Audio());

        var result = RecordingTranscoderRunner.ValidateProbes(input, output, 1_000_000, 700_000, new PluginConfiguration(), Main10());

        Assert.False(result.IsValid);
        Assert.Contains("not hevc", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MediaProbe Probe(params ProbeStream[] streams) => new(100, 12_000_000, streams);

    private static ProbeStream Video(string codec, string profile, double fps, string fieldOrder, bool captions)
        => new(0, "video", codec, profile, 1920, 1080, fps, fieldOrder, 10_000_000, captions, "bt709", "bt709", "bt709", "tv");

    private static ProbeStream Audio() => new(1, "audio", "ac3", "", 0, 0, 0, "", 384_000, false);

    private static ProbeStream Subtitle() => new(2, "subtitle", "dvb_subtitle", "", 0, 0, 0, "", 0, false);

    private static ProbeStream Attachment() => new(3, "attachment", "ttf", "", 0, 0, 0, "", 0, false);

    private static EncoderSelection Main10()
        => EncoderPlannerTests.Selection("hevc_videotoolbox", "hevc", true, "videotoolbox", "p010le", "main10");
}
