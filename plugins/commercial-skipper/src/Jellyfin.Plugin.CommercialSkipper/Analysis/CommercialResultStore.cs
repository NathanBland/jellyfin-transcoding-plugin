using Jellyfin.Plugin.CommercialSkipper.Models;
using Jellyfin.Plugin.RecordingPipeline;

namespace Jellyfin.Plugin.CommercialSkipper.Analysis;

public sealed class CommercialResultStore
{
    private readonly string _directory;

    public CommercialResultStore()
    {
        _directory = Path.Combine(
            Plugin.Instance?.DataDirectory ?? throw new InvalidOperationException("Plugin is not initialized."),
            "results");
        Directory.CreateDirectory(_directory);
    }

    public async Task<CommercialAnalysisRecord?> ReadAsync(Guid itemId, CancellationToken cancellationToken)
    {
        try
        {
            return await AtomicJsonStore.ReadAsync<CommercialAnalysisRecord>(GetPath(itemId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public Task WriteAsync(CommercialAnalysisRecord record, CancellationToken cancellationToken)
        => AtomicJsonStore.WriteAsync(GetPath(record.ItemId), record, cancellationToken);

    public void Delete(Guid itemId)
    {
        var path = GetPath(itemId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public IReadOnlyList<Guid> GetItemIds()
    {
        return Directory.EnumerateFiles(_directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => Guid.TryParseExact(name, "N", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToArray();
    }

    public IReadOnlyList<CommercialAnalysisRecord> ReadAll()
    {
        var records = new List<CommercialAnalysisRecord>();
        foreach (var itemId in GetItemIds())
        {
            try
            {
                var record = ReadAsync(itemId, CancellationToken.None).GetAwaiter().GetResult();
                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
            }
        }

        return records;
    }

    private string GetPath(Guid itemId) => Path.Combine(_directory, $"{itemId:N}.json");
}
