using Jellyfin.Plugin.TranscodingPolicy.Patching;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TranscodingPolicy.Startup;

/// <summary>
/// Applies the patch after Jellyfin has constructed its service provider.
/// </summary>
internal sealed class PatchHostedService : IHostedService
{
    private readonly ILogger<PatchHostedService> _logger;

    public PatchHostedService(ILogger<PatchHostedService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        EncodingPolicyPatch.Install(_logger, static () => Plugin.Instance?.Configuration);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        EncodingPolicyPatch.Uninstall();
        return Task.CompletedTask;
    }
}

