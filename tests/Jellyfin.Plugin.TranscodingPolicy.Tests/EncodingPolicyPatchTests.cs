using System.Runtime.CompilerServices;
using Jellyfin.Plugin.TranscodingPolicy.Configuration;
using Jellyfin.Plugin.TranscodingPolicy.Patching;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.TranscodingPolicy.Tests;

public sealed class EncodingPolicyPatchTests : IDisposable
{
    public void Dispose()
    {
        EncodingPolicyPatch.Uninstall();
    }

    [Fact]
    public void JellyfinController_HasExpectedVersionAndTargetSignature()
    {
        Assert.Equal(
            EncodingPolicyPatch.SupportedServerVersion,
            typeof(EncodingHelper).Assembly.GetName().Version);
        Assert.NotNull(EncodingPolicyPatch.FindTargetMethod());
    }

    [Fact]
    public void PluginAssembly_EmbedsConfigurationPage()
    {
        Assert.Contains(
            "Jellyfin.Plugin.TranscodingPolicy.Configuration.configPage.html",
            typeof(Plugin).Assembly.GetManifestResourceNames());
    }

    [Fact]
    public void InstalledPatch_MatchingJob_ReturnsSoftwareEncoder()
    {
        var configuration = new PluginConfiguration();
        EncodingPolicyPatch.Install(NullLogger.Instance, () => configuration);

        Assert.True(EncodingPolicyPatch.Status.Active, EncodingPolicyPatch.Status.Message);

        var target = EncodingPolicyPatch.FindTargetMethod();
        Assert.NotNull(target);

        var helper = RuntimeHelpers.GetUninitializedObject(typeof(EncodingHelper));
        var result = target.Invoke(
            helper,
            [
                "libx264",
                "h264",
                TranscodingPolicyEvaluatorTests.CreateState("mpeg2video", isLive: true),
                TranscodingPolicyEvaluatorTests.CreateVideoToolboxOptions()
            ]);

        Assert.Equal("libx264", result);
    }

    [Fact]
    public void Install_IsIdempotent()
    {
        var configuration = new PluginConfiguration();
        EncodingPolicyPatch.Install(NullLogger.Instance, () => configuration);
        EncodingPolicyPatch.Install(NullLogger.Instance, () => configuration);

        var target = EncodingPolicyPatch.FindTargetMethod();
        Assert.NotNull(target);
        var owners = HarmonyLib.Harmony.GetPatchInfo(target)?.Owners;

        Assert.NotNull(owners);
        Assert.Equal(1, owners!.Count(owner => owner == EncodingPolicyPatch.HarmonyId));
    }
}
