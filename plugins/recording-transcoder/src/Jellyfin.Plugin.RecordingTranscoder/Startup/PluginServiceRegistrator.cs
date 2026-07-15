using Jellyfin.Plugin.RecordingPipeline;
using Jellyfin.Plugin.RecordingTranscoder.Services;
using Jellyfin.Plugin.RecordingTranscoder.Transcoding;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RecordingTranscoder.Startup;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ProcessRunner>();
        serviceCollection.AddSingleton<LibraryScopeResolver>();
        serviceCollection.AddSingleton(provider => new PipelineLeaseManager(
            applicationHost.Resolve<IApplicationPaths>().DataPath,
            staleLeaseLogger: message => provider.GetRequiredService<ILogger<PipelineLeaseManager>>().LogWarning("{Message}", message)));
        serviceCollection.AddSingleton<TranscodeStore>();
        serviceCollection.AddSingleton<MediaProbeService>();
        serviceCollection.AddSingleton<RecordingTranscoderRunner>();
        serviceCollection.AddSingleton<TranscodeJobService>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<TranscodeJobService>());
    }
}
