using System.Reflection;
using System.Runtime.Loader;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.TranscodingPolicy.LoadContextTests;

public sealed class CollectibleLoadContextTests
{
    [Fact]
    public async Task HostedService_InstallsPatchFromCollectiblePluginContext()
    {
        _ = typeof(EncodingHelper).Assembly;
        _ = typeof(IHostedService).Assembly;

        var pluginAssemblyPath = GetPluginAssemblyPath();
        Assert.True(File.Exists(pluginAssemblyPath), pluginAssemblyPath);

        var pluginLoadContext = new AssemblyLoadContext(
            "TranscodingPolicyRegressionTest",
            isCollectible: true);
        pluginLoadContext.Resolving += ResolveFromDefaultContext;

        IHostedService? hostedService = null;
        try
        {
            var pluginAssembly = pluginLoadContext.LoadFromAssemblyPath(pluginAssemblyPath);
            Assert.Same(pluginLoadContext, AssemblyLoadContext.GetLoadContext(pluginAssembly));

            var hostedServiceType = pluginAssembly.GetType(
                "Jellyfin.Plugin.TranscodingPolicy.Startup.PatchHostedService",
                throwOnError: true)!;
            var loggerType = typeof(NullLogger<>).MakeGenericType(hostedServiceType);
            var logger = Activator.CreateInstance(loggerType);
            hostedService = Assert.IsAssignableFrom<IHostedService>(
                Activator.CreateInstance(hostedServiceType, logger));

            await hostedService.StartAsync(CancellationToken.None);

            var patchType = pluginAssembly.GetType(
                "Jellyfin.Plugin.TranscodingPolicy.Patching.EncodingPolicyPatch",
                throwOnError: true)!;
            var status = patchType.GetProperty(
                "Status",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var active = (bool)status.GetType().GetProperty("Active")!.GetValue(status)!;
            var message = (string)status.GetType().GetProperty("Message")!.GetValue(status)!;
            var prefixCount = (int)patchType.GetProperty(
                "InstalledPrefixCount",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

            Assert.True(active, message);
            Assert.Equal(1, prefixCount);

            var harmonyAssembly = AssemblyLoadContext.All
                .Where(context => !context.IsCollectible)
                .SelectMany(context => context.Assemblies)
                .Single(assembly =>
                    assembly.GetName().Name == "0Harmony"
                    && assembly.GetName().Version == new Version(2, 4, 2, 0));
            Assert.False(AssemblyLoadContext.GetLoadContext(harmonyAssembly)!.IsCollectible);
        }
        finally
        {
            if (hostedService is not null)
            {
                await hostedService.StopAsync(CancellationToken.None);
            }

            pluginLoadContext.Resolving -= ResolveFromDefaultContext;
            pluginLoadContext.Unload();
        }
    }

    private static string GetPluginAssemblyPath()
    {
        var metadata = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "PluginAssemblyPath");
        return Path.GetFullPath(metadata.Value!);
    }

    private static Assembly? ResolveFromDefaultContext(
        AssemblyLoadContext loadContext,
        AssemblyName assemblyName)
    {
        var existingAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
            assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
        if (existingAssembly is not null)
        {
            return existingAssembly;
        }

        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
