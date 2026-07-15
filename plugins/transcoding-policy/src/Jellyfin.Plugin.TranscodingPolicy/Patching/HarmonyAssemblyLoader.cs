using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TranscodingPolicy.Patching;

/// <summary>
/// Loads Harmony outside Jellyfin's collectible plugin load context.
/// </summary>
internal static class HarmonyAssemblyLoader
{
    internal const string HarmonyAssemblyName = "0Harmony";
    internal const string HarmonyResourceName =
        "Jellyfin.Plugin.TranscodingPolicy.Dependencies.0Harmony.dll";

    internal static readonly Version HarmonyVersion = new(2, 4, 2, 0);

    private static readonly object SyncRoot = new();
    private static Assembly? _harmonyAssembly;
    private static bool _resolverRegistered;

    internal static bool IsLoaded
    {
        get
        {
            lock (SyncRoot)
            {
                return _harmonyAssembly is not null;
            }
        }
    }

    internal static Assembly EnsureLoaded(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        lock (SyncRoot)
        {
            if (_harmonyAssembly is not null)
            {
                return _harmonyAssembly;
            }

            var existingAssembly = AssemblyLoadContext.All
                .Where(context => !context.IsCollectible)
                .SelectMany(context => context.Assemblies)
                .FirstOrDefault(IsExpectedHarmonyAssembly);
            if (existingAssembly is not null)
            {
                _harmonyAssembly = existingAssembly;
                RegisterResolver();
                logger.LogInformation(
                    "Transcoding Policy is reusing {Assembly} from non-collectible load context {LoadContext}",
                    existingAssembly.FullName,
                    AssemblyLoadContext.GetLoadContext(existingAssembly)?.Name ?? "Default");
                return existingAssembly;
            }

            var pluginAssembly = typeof(HarmonyAssemblyLoader).Assembly;
            using var resourceStream = pluginAssembly.GetManifestResourceStream(HarmonyResourceName)
                ?? throw new FileNotFoundException(
                    $"Embedded Harmony resource {HarmonyResourceName} was not found.");

            var loadContext = new AssemblyLoadContext(
                "Jellyfin.Plugin.TranscodingPolicy.Harmony",
                isCollectible: false);
            var loadedAssembly = loadContext.LoadFromStream(resourceStream);
            if (!IsExpectedHarmonyAssembly(loadedAssembly))
            {
                throw new FileLoadException(
                    $"Embedded patch runtime was {loadedAssembly.FullName}; expected "
                    + $"{HarmonyAssemblyName}, Version={HarmonyVersion}.");
            }

            _harmonyAssembly = loadedAssembly;
            RegisterResolver();

            logger.LogInformation(
                "Transcoding Policy loaded embedded {Assembly} into non-collectible load context {LoadContext}",
                loadedAssembly.FullName,
                loadContext.Name);
            return loadedAssembly;
        }
    }

    private static bool IsExpectedHarmonyAssembly(Assembly assembly)
    {
        var name = assembly.GetName();
        return string.Equals(name.Name, HarmonyAssemblyName, StringComparison.Ordinal)
            && name.Version == HarmonyVersion;
    }

    private static void RegisterResolver()
    {
        if (_resolverRegistered)
        {
            return;
        }

        AppDomain.CurrentDomain.AssemblyResolve += ResolveHarmonyAssembly;
        _resolverRegistered = true;
    }

    private static Assembly? ResolveHarmonyAssembly(object? sender, ResolveEventArgs args)
    {
        lock (SyncRoot)
        {
            if (_harmonyAssembly is null)
            {
                return null;
            }

            var requestedName = new AssemblyName(args.Name);
            return string.Equals(requestedName.Name, HarmonyAssemblyName, StringComparison.Ordinal)
                && requestedName.Version == HarmonyVersion
                    ? _harmonyAssembly
                    : null;
        }
    }
}
