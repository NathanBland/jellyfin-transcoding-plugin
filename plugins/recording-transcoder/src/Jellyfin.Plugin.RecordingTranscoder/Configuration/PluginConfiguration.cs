using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RecordingTranscoder.Configuration;

public enum EncoderMode
{
    FollowJellyfin,
    HardwareOnly,
    SoftwareOnly,
    Auto = FollowJellyfin,
    HevcVideoToolboxMain10 = 3,
    HevcVideoToolboxMain = 4
}

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool AutomaticTranscoding { get; set; }

    public bool FollowRecordingLibraries { get; set; } = true;

    public string[] SelectedLibraryIds { get; set; } = [];

    public EncoderMode Encoder { get; set; } = EncoderMode.FollowJellyfin;

    public double BitrateMultiplier { get; set; } = 1.0;

    public int MinimumSavingsPercent { get; set; } = 20;

    public int StabilitySeconds { get; set; } = 60;

    public int MaxConcurrentJobs { get; set; } = 1;

    public int TimeoutMinutes { get; set; } = 720;

    public long FreeSpaceHeadroomBytes { get; set; } = 1024L * 1024 * 1024;
}
