using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CommercialSkipper.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool AutomaticAnalysis { get; set; } = true;

    public bool FollowRecordingLibraries { get; set; } = true;

    public string[] SelectedLibraryIds { get; set; } = [];

    public string ComskipPath { get; set; } = string.Empty;

    public string CustomIniPath { get; set; } = string.Empty;

    public int CompletionDelaySeconds { get; set; } = 120;

    public int StabilitySeconds { get; set; } = 60;

    public int MaxConcurrentJobs { get; set; } = 1;

    public int ComskipThreads { get; set; } = 2;

    public bool PlayNice { get; set; } = true;

    public int TimeoutMinutes { get; set; } = 360;

    public bool KeepFailedJobDiagnostics { get; set; } = true;
}
