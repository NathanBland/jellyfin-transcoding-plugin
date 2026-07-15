using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.CommercialSkipper.Analysis;
using Jellyfin.Plugin.CommercialSkipper.Configuration;
using Jellyfin.Plugin.CommercialSkipper.Models;
using Jellyfin.Plugin.RecordingPipeline;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;

namespace Jellyfin.Plugin.CommercialSkipper.Providers;

public sealed class CommercialSegmentProvider(
    CommercialResultStore resultStore,
    LibraryScopeResolver scopeResolver,
    ILibraryManager libraryManager) : IMediaSegmentProvider
{
    public string Name => "Commercial Skipper";

    public ValueTask<bool> Supports(BaseItem item)
        => ValueTask.FromResult(item is Video && item.IsFileProtocol);

    public async Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(
        MediaSegmentGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var item = Plugin.Instance is null ? null : libraryManager.GetItemById(request.ItemId);
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (item is null
            || string.IsNullOrWhiteSpace(item.Path)
            || !scopeResolver.IsInScope(item.Path, configuration.FollowRecordingLibraries, configuration.SelectedLibraryIds))
        {
            return [];
        }

        var record = await resultStore.ReadAsync(request.ItemId, cancellationToken).ConfigureAwait(false);
        if (record is null
            || (record.Status != CommercialAnalysisStatus.Succeeded && record.Segments.Count == 0))
        {
            return [];
        }

        SourceFingerprint fingerprint;
        try
        {
            fingerprint = SourceFingerprint.FromPath(item.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (record.SourceFingerprint != fingerprint)
        {
            return [];
        }

        return record.Segments.Select(segment => new MediaSegmentDto
            {
                ItemId = request.ItemId,
                Type = MediaSegmentType.Commercial,
                StartTicks = segment.StartTicks,
                EndTicks = segment.EndTicks
            })
            .ToArray();
    }

}
