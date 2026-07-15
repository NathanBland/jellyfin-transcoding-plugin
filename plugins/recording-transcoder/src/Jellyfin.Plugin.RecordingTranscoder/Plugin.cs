using System.Globalization;
using Jellyfin.Plugin.RecordingTranscoder.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.RecordingTranscoder;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = Guid.Parse("6dbb9479-cd8e-4b64-8c10-70e64c90a809");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        DataDirectory = Path.Combine(applicationPaths.DataPath, "recording-transcoder");
        Directory.CreateDirectory(DataDirectory);
    }

    public static Plugin? Instance { get; private set; }

    public string DataDirectory { get; }

    public override string Name => "Recording Transcoder";

    public override string Description => "Safely recompresses completed DVR recordings with Jellyfin hardware or software encoders.";

    public override Guid Id => PluginId;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            DisplayName = Name,
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };
    }
}
