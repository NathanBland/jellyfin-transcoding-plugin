using System.Text.Json;

namespace Jellyfin.Plugin.RecordingPipeline;

public sealed class PipelineLeaseManager
{
    private readonly string _leaseDirectory;
    private readonly TimeSpan _staleAfter;
    private readonly Action<string>? _staleLeaseLogger;

    public PipelineLeaseManager(
        string dataPath,
        TimeSpan? staleAfter = null,
        Action<string>? staleLeaseLogger = null)
    {
        _leaseDirectory = Path.Combine(dataPath, "recording-pipeline", "v1");
        _staleAfter = staleAfter ?? TimeSpan.FromHours(12);
        _staleLeaseLogger = staleLeaseLogger;
        Directory.CreateDirectory(_leaseDirectory);
    }

    public string GetPath(Guid itemId) => Path.Combine(_leaseDirectory, $"{itemId:N}.json");

    private string GetLockPath(Guid itemId) => Path.Combine(_leaseDirectory, $".{itemId:N}.lock");

    public async Task<PipelineLeaseDocument?> TryAcquireAsync(
        Guid itemId,
        string sourcePath,
        SourceFingerprint fingerprint,
        Guid ownerPluginId,
        PipelineStage stage,
        PipelineLeaseState state,
        CancellationToken cancellationToken)
    {
        var path = GetPath(itemId);
        await using var itemLock = await AcquireItemLockAsync(itemId, cancellationToken).ConfigureAwait(false);
        await RemoveIfStaleCoreAsync(path, cancellationToken).ConfigureAwait(false);

        var lease = new PipelineLeaseDocument
        {
            ItemId = itemId,
            SourcePath = sourcePath,
            SourceFingerprint = fingerprint,
            OwnerPluginId = ownerPluginId,
            Stage = stage,
            State = state,
            LeaseId = Guid.NewGuid(),
            UpdatedUtc = DateTime.UtcNow
        };

        var created = false;
        var committed = false;
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            created = true;
            await JsonSerializer.SerializeAsync(stream, lease, AtomicJsonStore.Options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            committed = true;
            return lease;
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            if (created && !committed && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _staleLeaseLogger?.Invoke($"Unable to remove incomplete recording-pipeline lease {Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }
    }

    public async Task<PipelineLeaseDocument?> GetActiveAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var path = GetPath(itemId);
        await using var itemLock = await AcquireItemLockAsync(itemId, cancellationToken).ConfigureAwait(false);
        await RemoveIfStaleCoreAsync(path, cancellationToken).ConfigureAwait(false);
        return await ReadCoreAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PipelineLeaseDocument?> RefreshAsync(
        PipelineLeaseDocument lease,
        PipelineLeaseState state,
        CancellationToken cancellationToken,
        PipelineLeaseState? expectedState = null)
    {
        var path = GetPath(lease.ItemId);
        await using var itemLock = await AcquireItemLockAsync(lease.ItemId, cancellationToken).ConfigureAwait(false);
        await RemoveIfStaleCoreAsync(path, cancellationToken).ConfigureAwait(false);
        var current = await ReadCoreAsync(path, cancellationToken).ConfigureAwait(false);
        if (current?.LeaseId != lease.LeaseId
            || current.OwnerPluginId != lease.OwnerPluginId
            || (expectedState.HasValue && current.State != expectedState.Value))
        {
            return null;
        }

        var refreshed = lease with { State = state, UpdatedUtc = DateTime.UtcNow };
        await AtomicJsonStore.WriteAsync(path, refreshed, cancellationToken).ConfigureAwait(false);
        return refreshed;
    }

    public async Task ReleaseAsync(PipelineLeaseDocument lease, CancellationToken cancellationToken)
    {
        var path = GetPath(lease.ItemId);
        await using var itemLock = await AcquireItemLockAsync(lease.ItemId, cancellationToken).ConfigureAwait(false);
        await RemoveIfStaleCoreAsync(path, cancellationToken).ConfigureAwait(false);
        var current = await ReadCoreAsync(path, cancellationToken).ConfigureAwait(false);
        if (current?.LeaseId == lease.LeaseId && current.OwnerPluginId == lease.OwnerPluginId)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _staleLeaseLogger?.Invoke($"Unable to release recording-pipeline lease {Path.GetFileName(path)}: {ex.Message}");
            }
        }
    }

    private async Task<FileStream> AcquireItemLockAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var lockPath = GetLockPath(itemId);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<PipelineLeaseDocument?> ReadCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await AtomicJsonStore.ReadAsync<PipelineLeaseDocument>(path, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task RemoveIfStaleCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        PipelineLeaseDocument? current = null;
        try
        {
            current = await AtomicJsonStore.ReadAsync<PipelineLeaseDocument>(path, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // A corrupt lease may be recovered using its file timestamp below.
        }
        catch (IOException)
        {
            return;
        }

        var updatedUtc = current?.UpdatedUtc ?? File.GetLastWriteTimeUtc(path);
        if (DateTime.UtcNow - updatedUtc <= _staleAfter)
        {
            return;
        }

        try
        {
            File.Delete(path);
            _staleLeaseLogger?.Invoke($"Recovered abandoned recording-pipeline lease {Path.GetFileName(path)} last updated {updatedUtc:O}.");
        }
        catch (IOException)
        {
            // Another worker still owns or replaced the lease.
        }
    }
}
