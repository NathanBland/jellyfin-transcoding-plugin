namespace Jellyfin.Plugin.TranscodingPolicy.Policy;

/// <summary>
/// Result of evaluating a transcode against the configured policy.
/// </summary>
internal readonly record struct PolicyDecision(bool ForceSoftwareEncoder, string Reason)
{
    public static PolicyDecision UseJellyfinDefault(string reason) => new(false, reason);

    public static PolicyDecision UseSoftwareEncoder(string reason) => new(true, reason);
}

