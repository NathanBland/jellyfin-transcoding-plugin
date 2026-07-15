using System.Runtime.CompilerServices;
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
        try
        {
            HarmonyAssemblyLoader.EnsureLoaded(_logger);
            InstallPatch(_logger);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Transcoding Policy failed to load its patch runtime and will remain inactive");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (HarmonyAssemblyLoader.IsLoaded)
        {
            UninstallPatch();
        }

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InstallPatch(ILogger logger)
        => EncodingPolicyPatch.Install(logger, static () => Plugin.Instance?.Configuration);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UninstallPatch()
        => EncodingPolicyPatch.Uninstall();
}
