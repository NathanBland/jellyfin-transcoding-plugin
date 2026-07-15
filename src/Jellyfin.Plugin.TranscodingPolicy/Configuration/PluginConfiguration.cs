using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TranscodingPolicy.Configuration;

/// <summary>
/// Persistent plugin settings.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether any policy may be applied.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the MPEG-2 workaround rule is enabled.
    /// </summary>
    public bool EnableSoftwareEncodingRule { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether matching is limited to live streams.
    /// </summary>
    public bool LiveStreamsOnly { get; set; } = true;

    /// <summary>
    /// Gets or sets input codecs matched by the rule.
    /// </summary>
    public string[] InputCodecs { get; set; } = ["mpeg2video"];

    /// <summary>
    /// Gets or sets output codecs matched by the rule.
    /// </summary>
    public string[] OutputCodecs { get; set; } = ["h264"];

    /// <summary>
    /// Gets or sets a value indicating whether successful policy decisions are logged.
    /// </summary>
    public bool EnableDecisionLogging { get; set; } = true;
}

