namespace Jellyfin.Plugin.TranscodingPolicy.Patching;

/// <summary>
/// Current compatibility and activation state of the runtime patch.
/// </summary>
public sealed record PatchStatus(
    bool Active,
    bool Compatible,
    string Message,
    string ServerVersion,
    string TargetVersion,
    string TargetMethod,
    DateTimeOffset? InstalledAtUtc)
{
    internal static PatchStatus NotStarted { get; } = new(
        false,
        false,
        "Patch service has not started.",
        "Unknown",
        EncodingPolicyPatch.SupportedServerVersion.ToString(),
        EncodingPolicyPatch.TargetMethodDisplayName,
        null);
}

