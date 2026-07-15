using Jellyfin.Plugin.RecordingPipeline;

namespace Jellyfin.Plugin.CommercialSkipper.Tests;

public sealed class PipelineTests
{
    [Fact]
    public async Task Lease_AllowsOnlyOneOwnerAndReleaseRequiresOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new PipelineLeaseManager(directory);
            var itemId = Guid.NewGuid();
            var fingerprint = new SourceFingerprint(123, DateTime.UtcNow);
            var first = await manager.TryAcquireAsync(itemId, "/tmp/video.ts", fingerprint, Guid.NewGuid(), PipelineStage.Transcode, PipelineLeaseState.Pending, CancellationToken.None);
            var second = await manager.TryAcquireAsync(itemId, "/tmp/video.ts", fingerprint, Guid.NewGuid(), PipelineStage.CommercialScan, PipelineLeaseState.Running, CancellationToken.None);

            Assert.NotNull(first);
            Assert.Null(second);
            await manager.ReleaseAsync(first!, CancellationToken.None);
            Assert.Null(await manager.GetActiveAsync(itemId, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task Lease_AbandonedOwnerCannotRefreshOrReleaseReplacement()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new PipelineLeaseManager(directory, TimeSpan.FromHours(1));
            var itemId = Guid.NewGuid();
            var fingerprint = new SourceFingerprint(123, DateTime.UtcNow);
            var abandoned = await manager.TryAcquireAsync(itemId, "/tmp/video.ts", fingerprint, Guid.NewGuid(), PipelineStage.Transcode, PipelineLeaseState.Running, CancellationToken.None);
            Assert.NotNull(abandoned);

            await AtomicJsonStore.WriteAsync(
                manager.GetPath(itemId),
                abandoned! with { UpdatedUtc = DateTime.UtcNow.Subtract(TimeSpan.FromHours(2)) });
            var replacement = await manager.TryAcquireAsync(itemId, "/tmp/video.ts", fingerprint, Guid.NewGuid(), PipelineStage.CommercialScan, PipelineLeaseState.Running, CancellationToken.None);
            Assert.NotNull(replacement);

            Assert.Null(await manager.RefreshAsync(abandoned!, PipelineLeaseState.Running, CancellationToken.None));
            await manager.ReleaseAsync(abandoned!, CancellationToken.None);

            var active = await manager.GetActiveAsync(itemId, CancellationToken.None);
            Assert.Equal(replacement!.LeaseId, active?.LeaseId);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task Lease_StateTransitionRejectsDelayedPendingHeartbeat()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new PipelineLeaseManager(directory);
            var pending = await manager.TryAcquireAsync(
                Guid.NewGuid(),
                "/tmp/video.ts",
                new SourceFingerprint(123, DateTime.UtcNow),
                Guid.NewGuid(),
                PipelineStage.Transcode,
                PipelineLeaseState.Pending,
                CancellationToken.None);
            Assert.NotNull(pending);

            var running = await manager.RefreshAsync(
                pending!,
                PipelineLeaseState.Running,
                CancellationToken.None,
                PipelineLeaseState.Pending);
            Assert.NotNull(running);
            Assert.Null(await manager.RefreshAsync(
                pending!,
                PipelineLeaseState.Pending,
                CancellationToken.None,
                PipelineLeaseState.Pending));
            Assert.Equal(PipelineLeaseState.Running, (await manager.GetActiveAsync(pending!.ItemId, CancellationToken.None))?.State);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Theory]
    [InlineData("/recordings", "/recordings/show/video.ts", true)]
    [InlineData("/recordings", "/recordings2/video.ts", false)]
    [InlineData("/recordings", "/recordings", true)]
    public void ContainsPath_UsesDirectoryBoundaries(string root, string path, bool expected)
        => Assert.Equal(expected, LibraryScopeResolver.ContainsPath(root, path));

    [Fact]
    public void PreserveUnavailableSelections_KeepsMissingStableIdWithoutRemapping()
    {
        var libraries = new List<LibrarySelectionInfo>
        {
            new("available", "Recordings", ["/recordings"], true, true, true)
        };

        LibraryScopeResolver.PreserveUnavailableSelections(libraries, ["available", "missing-id"]);

        var missing = Assert.Single(libraries, library => library.Id == "missing-id");
        Assert.False(missing.IsAvailable);
        Assert.True(missing.IsExplicitlySelected);
        Assert.False(missing.IsEffective);
        Assert.Empty(missing.Locations);
    }

    [Fact]
    public void Plugin_EmbedsConfigurationPage()
        => Assert.Contains(
            "Jellyfin.Plugin.CommercialSkipper.Configuration.configPage.html",
            typeof(Plugin).Assembly.GetManifestResourceNames());
}
