namespace Jellyfin.Plugin.CommercialSkipper.Tests;

public sealed class ConfigurationPageTests
{
    [Fact]
    public void EmbeddedPageDoesNotContainTemplateLiterals()
    {
        var assembly = typeof(Plugin).Assembly;
        var resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(".Configuration.configPage.html", StringComparison.Ordinal));
        using var stream = Assert.IsAssignableFrom<Stream>(assembly.GetManifestResourceStream(resourceName));
        using var reader = new StreamReader(stream);
        var html = reader.ReadToEnd();

        Assert.DoesNotContain("${", html);
        Assert.DoesNotContain("`", html);
    }
}
