using Jellyfin.Plugin.CommercialSkipper.Analysis;
using Jellyfin.Plugin.CommercialSkipper.Configuration;

namespace Jellyfin.Plugin.CommercialSkipper.Tests;

public sealed class ComskipIniBuilderTests
{
    [Fact]
    public void Build_DefaultProfile_IsUsOtaAndEdlOnly()
    {
        var result = ComskipIniBuilder.Build(new PluginConfiguration());

        Assert.Contains("detect_method=43", result, StringComparison.Ordinal);
        Assert.Contains("intelligent_brightness=1", result, StringComparison.Ordinal);
        Assert.Contains("output_edl=1", result, StringComparison.Ordinal);
        Assert.Contains("hardware_decode=0", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_CustomProfile_OverridesUnsafeOutputValues()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "detect_method=1\noutput_edl=0\nhardware_decode=1\n");
            var result = ComskipIniBuilder.Build(new PluginConfiguration { CustomIniPath = path });

            Assert.Contains("detect_method=1", result, StringComparison.Ordinal);
            Assert.Contains("output_edl=1", result, StringComparison.Ordinal);
            Assert.Contains("hardware_decode=0", result, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
