using Jellyfin.Plugin.CommercialSkipper.Analysis;
using Jellyfin.Plugin.CommercialSkipper.Providers;
using Jellyfin.Plugin.CommercialSkipper.Services;
using Jellyfin.Plugin.RecordingPipeline;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CommercialSkipper.Startup;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ProcessRunner>();
        serviceCollection.AddSingleton<LibraryScopeResolver>();
        serviceCollection.AddSingleton(provider => new PipelineLeaseManager(
            applicationHost.Resolve<IApplicationPaths>().DataPath,
            staleLeaseLogger: message => provider.GetRequiredService<ILogger<PipelineLeaseManager>>().LogWarning("{Message}", message)));
        serviceCollection.AddSingleton<CommercialResultStore>();
        serviceCollection.AddSingleton<ComskipRunner>();
        serviceCollection.AddSingleton<IMediaSegmentProvider, CommercialSegmentProvider>();
        serviceCollection.AddSingleton<CommercialJobService>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<CommercialJobService>());
    }
}
