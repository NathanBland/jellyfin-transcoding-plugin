using System.Reflection;
using HarmonyLib;
using Jellyfin.Plugin.TranscodingPolicy.Configuration;
using Jellyfin.Plugin.TranscodingPolicy.Policy;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TranscodingPolicy.Patching;

/// <summary>
/// Owns the narrowly scoped patch to Jellyfin's encoder selector.
/// </summary>
internal static class EncodingPolicyPatch
{
    internal const string HarmonyId = "com.nathanbland.jellyfin.transcoding-policy";
    internal const string TargetMethodName = "GetH26xOrAv1Encoder";
    internal const string TargetMethodDisplayName = "EncodingHelper.GetH26xOrAv1Encoder";

    internal static readonly Version SupportedServerVersion = new(10, 11, 11, 0);

    private static readonly object SyncRoot = new();
    private static Func<PluginConfiguration?> _configurationAccessor = static () => null;
    private static Harmony? _harmony;
    private static ILogger? _logger;
    private static MethodInfo? _targetMethod;
    private static PatchStatus _status = PatchStatus.NotStarted;

    internal static PatchStatus Status
    {
        get
        {
            lock (SyncRoot)
            {
                return _status;
            }
        }
    }

    internal static void Install(ILogger logger, Func<PluginConfiguration?> configurationAccessor)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configurationAccessor);

        lock (SyncRoot)
        {
            _logger = logger;
            _configurationAccessor = configurationAccessor;

            var serverVersion = typeof(EncodingHelper).Assembly.GetName().Version;
            var serverVersionText = serverVersion?.ToString() ?? "Unknown";
            if (serverVersion != SupportedServerVersion)
            {
                _status = CreateStatus(
                    active: false,
                    compatible: false,
                    message: $"Unsupported Jellyfin.Controller version {serverVersionText}; expected {SupportedServerVersion}.",
                    serverVersionText,
                    installedAtUtc: null);
                logger.LogError("Transcoding Policy is inactive: {Message}", _status.Message);
                return;
            }

            var targetMethod = FindTargetMethod();
            if (targetMethod is null || !HasExpectedSignature(targetMethod))
            {
                _status = CreateStatus(
                    active: false,
                    compatible: false,
                    message: $"Required method {TargetMethodDisplayName} was not found with the expected signature.",
                    serverVersionText,
                    installedAtUtc: null);
                logger.LogError("Transcoding Policy is inactive: {Message}", _status.Message);
                return;
            }

            try
            {
                _harmony ??= new Harmony(HarmonyId);
                _targetMethod = targetMethod;

                var patchInfo = Harmony.GetPatchInfo(targetMethod);
                if (patchInfo?.Owners.Contains(HarmonyId, StringComparer.Ordinal) != true)
                {
                    var prefixMethod = AccessTools.Method(typeof(EncodingPolicyPatch), nameof(Prefix));
                    if (prefixMethod is null)
                    {
                        throw new MissingMethodException(typeof(EncodingPolicyPatch).FullName, nameof(Prefix));
                    }

                    _harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                }

                _status = CreateStatus(
                    active: true,
                    compatible: true,
                    message: "Patch is active and policy decisions are enabled.",
                    serverVersionText,
                    installedAtUtc: DateTimeOffset.UtcNow);
                logger.LogInformation(
                    "Transcoding Policy patch installed for {TargetMethod} on Jellyfin.Controller {ServerVersion}",
                    TargetMethodDisplayName,
                    serverVersionText);
            }
            catch (Exception exception)
            {
                _status = CreateStatus(
                    active: false,
                    compatible: true,
                    message: $"Patch installation failed: {exception.GetType().Name}: {exception.Message}",
                    serverVersionText,
                    installedAtUtc: null);
                logger.LogError(exception, "Transcoding Policy failed to install and will remain inactive");
            }
        }
    }

    internal static void Uninstall()
    {
        lock (SyncRoot)
        {
            try
            {
                if (_harmony is not null && _targetMethod is not null)
                {
                    _harmony.Unpatch(_targetMethod, HarmonyPatchType.All, HarmonyId);
                }

                _logger?.LogInformation("Transcoding Policy patch removed");
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, "Transcoding Policy failed to remove its runtime patch");
            }
            finally
            {
                var serverVersionText = typeof(EncodingHelper).Assembly.GetName().Version?.ToString() ?? "Unknown";
                _targetMethod = null;
                _harmony = null;
                _configurationAccessor = static () => null;
                _status = CreateStatus(
                    active: false,
                    compatible: true,
                    message: "Patch service is stopped.",
                    serverVersionText,
                    installedAtUtc: null);
            }
        }
    }

    internal static MethodInfo? FindTargetMethod()
        => AccessTools.Method(
            typeof(EncodingHelper),
            TargetMethodName,
            [typeof(string), typeof(string), typeof(EncodingJobInfo), typeof(EncodingOptions)]);

    private static bool HasExpectedSignature(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return method.IsPrivate
            && !method.IsStatic
            && method.ReturnType == typeof(string)
            && parameters.Length == 4
            && parameters[0].ParameterType == typeof(string)
            && parameters[1].ParameterType == typeof(string)
            && parameters[2].ParameterType == typeof(EncodingJobInfo)
            && parameters[3].ParameterType == typeof(EncodingOptions);
    }

    private static bool Prefix(
        string defaultEncoder,
        string hwEncoder,
        EncodingJobInfo state,
        EncodingOptions encodingOptions,
        ref string __result)
    {
        try
        {
            var configuration = _configurationAccessor();
            var decision = TranscodingPolicyEvaluator.Evaluate(state, encodingOptions, hwEncoder, configuration);
            if (!decision.ForceSoftwareEncoder)
            {
                return true;
            }

            __result = defaultEncoder;
            if (configuration?.EnableDecisionLogging == true)
            {
                _logger?.LogInformation(
                    "Transcoding Policy selected software encoder {Encoder}. {Reason}",
                    defaultEncoder,
                    decision.Reason);
            }

            return false;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Transcoding Policy evaluation failed; Jellyfin's default encoder selection will be used");
            return true;
        }
    }

    private static PatchStatus CreateStatus(
        bool active,
        bool compatible,
        string message,
        string serverVersion,
        DateTimeOffset? installedAtUtc)
        => new(
            active,
            compatible,
            message,
            serverVersion,
            SupportedServerVersion.ToString(),
            TargetMethodDisplayName,
            installedAtUtc);
}

