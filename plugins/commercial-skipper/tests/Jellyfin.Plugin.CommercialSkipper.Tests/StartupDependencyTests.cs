using Jellyfin.Plugin.CommercialSkipper.Analysis;
using Jellyfin.Plugin.CommercialSkipper.Providers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.CommercialSkipper.Tests;

public sealed class StartupDependencyTests
{
    [Fact]
    public void SegmentProviderHasOnlyCycleSafeDependencies()
    {
        var constructor = Assert.Single(typeof(CommercialSegmentProvider).GetConstructors());
        var parameterTypes = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Equal([typeof(CommercialResultStore), typeof(ILibraryManager)], parameterTypes);
    }

    [Fact]
    public void ResultStoreDoesNotRequirePluginInstanceDuringConstruction()
    {
        var constructor = Assert.Single(typeof(CommercialResultStore).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(IApplicationPaths), parameter.ParameterType);
    }
}
