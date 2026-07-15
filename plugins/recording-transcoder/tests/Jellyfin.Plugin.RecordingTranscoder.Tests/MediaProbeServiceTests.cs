using Jellyfin.Plugin.RecordingTranscoder.Transcoding;

namespace Jellyfin.Plugin.RecordingTranscoder.Tests;

public sealed class MediaProbeServiceTests
{
    [Fact]
    public void Parse_ReadsStreamsDurationAndCaptions()
    {
        const string json = """
            {"streams":[
              {"index":0,"codec_type":"video","codec_name":"mpeg2video","profile":"Main","width":1920,"height":1080,"avg_frame_rate":"30000/1001","field_order":"tt","bit_rate":"12000000","closed_captions":1,"color_space":"bt709","color_transfer":"bt709","color_primaries":"bt709","color_range":"tv"},
              {"index":1,"codec_type":"audio","codec_name":"ac3","bit_rate":"384000"}
            ],"format":{"duration":"3600.25","bit_rate":"14500000"}}
            """;

        var result = MediaProbeService.Parse(json);
        Assert.Equal(3600.25, result.DurationSeconds);
        Assert.Equal(14_500_000, result.BitRate);
        Assert.True(result.Video!.HasClosedCaptions);
        Assert.Equal(1, result.AudioCount);
        Assert.InRange(result.Video.FramesPerSecond, 29.96, 29.98);
        Assert.Equal("bt709", result.Video.ColorSpace);
    }

    [Fact]
    public void Plugin_EmbedsConfigurationPage()
        => Assert.Contains("Jellyfin.Plugin.RecordingTranscoder.Configuration.configPage.html", typeof(Plugin).Assembly.GetManifestResourceNames());

    [Fact]
    public void ParseVideoEncoders_ReturnsOnlyVideoEncoderNames()
    {
        const string output = """
             V....D libx264              libx264 H.264
             V..... hevc_vaapi           H.265/HEVC (VAAPI)
             A..... aac                  AAC
             V..... = Video
            """;

        var encoders = RecordingTranscoderRunner.ParseVideoEncoders(output);

        Assert.Equal(2, encoders.Count);
        Assert.Contains("libx264", encoders);
        Assert.Contains("hevc_vaapi", encoders);
    }
}
