using Jellyfin.Plugin.RecordingPipeline;
using Jellyfin.Plugin.RecordingTranscoder.Models;

namespace Jellyfin.Plugin.RecordingTranscoder.Transcoding;

public sealed class TranscodeStore
{
    private readonly string _resultDirectory;
    private readonly string _transactionDirectory;

    public TranscodeStore()
    {
        var root = Plugin.Instance?.DataDirectory ?? throw new InvalidOperationException("Plugin is not initialized.");
        _resultDirectory = Path.Combine(root, "results");
        _transactionDirectory = Path.Combine(root, "transactions");
        Directory.CreateDirectory(_resultDirectory);
        Directory.CreateDirectory(_transactionDirectory);
    }

    public Task<TranscodeRecord?> ReadAsync(Guid itemId, CancellationToken cancellationToken)
        => AtomicJsonStore.ReadAsync<TranscodeRecord>(Path.Combine(_resultDirectory, $"{itemId:N}.json"), cancellationToken);

    public Task WriteAsync(TranscodeRecord record, CancellationToken cancellationToken)
        => AtomicJsonStore.WriteAsync(Path.Combine(_resultDirectory, $"{record.ItemId:N}.json"), record, cancellationToken);

    public Task WriteJournalAsync(TransactionJournal journal, CancellationToken cancellationToken)
        => AtomicJsonStore.WriteAsync(GetJournalPath(journal.JobId), journal, cancellationToken);

    public void DeleteJournal(Guid jobId)
    {
        var path = GetJournalPath(jobId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public IReadOnlyList<TransactionJournal> ReadJournals()
    {
        var journals = new List<TransactionJournal>();
        foreach (var path in Directory.EnumerateFiles(_transactionDirectory, "*.json"))
        {
            try
            {
                var journal = AtomicJsonStore.ReadAsync<TransactionJournal>(path).GetAwaiter().GetResult();
                if (journal is not null)
                {
                    journals.Add(journal);
                }
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
            }
        }

        return journals;
    }

    public IReadOnlyList<TranscodeRecord> ReadAll()
    {
        var records = new List<TranscodeRecord>();
        foreach (var path in Directory.EnumerateFiles(_resultDirectory, "*.json"))
        {
            try
            {
                var record = AtomicJsonStore.ReadAsync<TranscodeRecord>(path).GetAwaiter().GetResult();
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

    private string GetJournalPath(Guid jobId) => Path.Combine(_transactionDirectory, $"{jobId:N}.json");
}
