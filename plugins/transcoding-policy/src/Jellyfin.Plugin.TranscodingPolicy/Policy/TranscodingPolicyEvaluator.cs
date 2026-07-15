using Jellyfin.Plugin.TranscodingPolicy.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.TranscodingPolicy.Policy;

/// <summary>
/// Evaluates the plugin's rule without mutating Jellyfin job state.
/// </summary>
internal static class TranscodingPolicyEvaluator
{
    public static PolicyDecision Evaluate(
        EncodingJobInfo? state,
        EncodingOptions? encodingOptions,
        string? outputCodec,
        PluginConfiguration? configuration)
    {
        if (configuration is null || !configuration.IsEnabled)
        {
            return PolicyDecision.UseJellyfinDefault("Plugin is disabled.");
        }

        if (!configuration.EnableSoftwareEncodingRule)
        {
            return PolicyDecision.UseJellyfinDefault("Software-encoding rule is disabled.");
        }

        if (state is null || encodingOptions is null)
        {
            return PolicyDecision.UseJellyfinDefault("Encoding job information is unavailable.");
        }

        if (encodingOptions.HardwareAccelerationType != HardwareAccelerationType.videotoolbox)
        {
            return PolicyDecision.UseJellyfinDefault("Apple VideoToolbox is not selected.");
        }

        if (!encodingOptions.EnableHardwareEncoding)
        {
            return PolicyDecision.UseJellyfinDefault("Jellyfin hardware encoding is already disabled.");
        }

        var inputCodec = state.VideoStream?.Codec;
        if (!ContainsCodec(configuration.InputCodecs, inputCodec))
        {
            return PolicyDecision.UseJellyfinDefault("Input codec does not match.");
        }

        if (!ContainsCodec(configuration.OutputCodecs, outputCodec))
        {
            return PolicyDecision.UseJellyfinDefault("Output codec does not match.");
        }

        var isLiveStream = !string.IsNullOrWhiteSpace(state.BaseRequest?.LiveStreamId)
            || !string.IsNullOrWhiteSpace(state.MediaSource?.LiveStreamId);

        if (configuration.LiveStreamsOnly && !isLiveStream)
        {
            return PolicyDecision.UseJellyfinDefault("Job is not a live stream.");
        }

        return PolicyDecision.UseSoftwareEncoder(
            $"Matched input codec '{inputCodec}', output codec '{outputCodec}', live stream: {isLiveStream}.");
    }

    private static bool ContainsCodec(IEnumerable<string>? codecs, string? candidate)
    {
        if (codecs is null || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalizedCandidate = candidate.Trim();
        return codecs.Any(codec => string.Equals(
            codec?.Trim(),
            normalizedCandidate,
            StringComparison.OrdinalIgnoreCase));
    }
}

