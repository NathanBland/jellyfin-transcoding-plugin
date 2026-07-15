using System.Globalization;
using Jellyfin.Plugin.CommercialSkipper.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.CommercialSkipper;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginId = Guid.Parse("19323a5b-d8ac-45f4-a1c5-3832498c58bc");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        DataDirectory = Path.Combine(applicationPaths.DataPath, "commercial-skipper");
        Directory.CreateDirectory(DataDirectory);
    }

    public static Plugin? Instance { get; private set; }

    public string DataDirectory { get; }

    public override string Name => "Commercial Skipper";

    public override string Description => "Detects commercials in recordings with Comskip and publishes native Jellyfin media segments.";

    public override Guid Id => PluginId;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            DisplayName = Name,
            EmbeddedResourcePath = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.Configuration.configPage.html",
                GetType().Namespace)
        };
    }
}
