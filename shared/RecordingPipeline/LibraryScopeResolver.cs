using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;

namespace Jellyfin.Plugin.RecordingPipeline;

public sealed class LibraryScopeResolver(
    ILibraryManager libraryManager,
    IRecordingsManager recordingsManager)
{
    public IReadOnlyList<LibrarySelectionInfo> GetLibraries(bool followRecordingLibraries, IReadOnlyCollection<string> selectedLibraryIds)
    {
        var selected = selectedLibraryIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dvrLocations = recordingsManager.GetRecordingFolders()
            .SelectMany(folder => folder.Locations)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var libraries = libraryManager.GetVirtualFolders()
            .Select(folder =>
            {
                var locations = folder.Locations
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(NormalizePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var isDvr = locations.Any(dvrLocations.Contains);
                var isSelected = selected.Contains(folder.ItemId);
                return new LibrarySelectionInfo(
                    folder.ItemId,
                    folder.Name,
                    locations,
                    isDvr,
                    isSelected,
                    isSelected || (followRecordingLibraries && isDvr),
                    true);
            })
            .ToList();

        PreserveUnavailableSelections(libraries, selected);

        return libraries
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static void PreserveUnavailableSelections(
        ICollection<LibrarySelectionInfo> libraries,
        IReadOnlyCollection<string> selectedLibraryIds)
    {
        var availableIds = libraries.Select(info => info.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in selectedLibraryIds.Where(id => !availableIds.Contains(id)))
        {
            libraries.Add(new LibrarySelectionInfo(
                id,
                $"Unavailable library ({id})",
                [],
                false,
                true,
                false,
                false));
        }
    }

    public IReadOnlyList<string> GetEffectiveRoots(bool followRecordingLibraries, IReadOnlyCollection<string> selectedLibraryIds)
    {
        var roots = GetLibraries(followRecordingLibraries, selectedLibraryIds)
            .Where(info => info.IsEffective)
            .SelectMany(info => info.Locations)
            .ToList();

        if (followRecordingLibraries)
        {
            roots.AddRange(recordingsManager.GetRecordingFolders().SelectMany(folder => folder.Locations));
        }

        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsInScope(string? path, bool followRecordingLibraries, IReadOnlyCollection<string> selectedLibraryIds)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        return GetEffectiveRoots(followRecordingLibraries, selectedLibraryIds)
            .Any(root => ContainsPath(root, normalizedPath));
    }

    public IReadOnlyList<BaseItem> GetScopedVideos(bool followRecordingLibraries, IReadOnlyCollection<string> selectedLibraryIds)
    {
        return libraryManager.GetItemList(new InternalItemsQuery
        {
            Recursive = true,
            IsFolder = false,
            IsVirtualItem = false,
            MediaTypes = [MediaType.Video]
        })
            .Where(item => item.IsFileProtocol)
            .Where(item => IsInScope(item.Path, followRecordingLibraries, selectedLibraryIds))
            .ToArray();
    }

    public static bool ContainsPath(string root, string candidate)
    {
        var normalizedRoot = NormalizePath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = NormalizePath(candidate);
        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);
}
